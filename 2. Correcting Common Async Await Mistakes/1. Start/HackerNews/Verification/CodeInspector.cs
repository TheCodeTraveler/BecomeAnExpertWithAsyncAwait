using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace HackerNews;

// Workshop plumbing: reads a type's compiled code, including the lambdas and async state machines the compiler generates for it.
// A few requirements cannot always be seen from outside, such as a blocking wait that happens to run off Blazor's renderer, so a step reads the code instead.
public static class CodeInspector
{
	const BindingFlags _declaredMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

	static readonly Dictionary<short, OpCode> _opCodes = typeof(OpCodes)
		.GetFields(BindingFlags.Public | BindingFlags.Static)
		.Select(static field => field.GetValue(null) is OpCode opCode ? opCode : throw new InvalidOperationException($"OpCodes.{field.Name} is not an OpCode"))
		.ToDictionary(static opCode => opCode.Value);

	// Every method the type's code calls, written as Type.Method, once per call. A method called from two places is listed twice.
	public static IReadOnlyList<string> GetCalledMethods(Type type)
	{
		List<string> calledMethods = [];

		foreach (var declaringType in GetTypeAndNestedTypes(type))
		{
			var methods = declaringType.GetMethods(_declaredMembers).Cast<MethodBase>().Concat(declaringType.GetConstructors(_declaredMembers));

			foreach (var method in methods)
			{
				AddCalledMethods(method, calledMethods);
			}
		}

		return calledMethods;
	}

	// async void methods, including async lambdas the compiler turned into async void methods
	public static IReadOnlyList<string> GetAsyncVoidMethods(Type type) =>
	[
		.. GetTypeAndNestedTypes(type)
			.SelectMany(static declaringType => declaringType.GetMethods(_declaredMembers))
			.Where(static method => method.ReturnType == typeof(void) && method.IsDefined(typeof(AsyncStateMachineAttribute), false))
			.Select(static method => DescribeMethod(method.Name)),
	];

	public static MethodInfo? FindMethod(Type type, string name) => type.GetMethods(_declaredMembers).FirstOrDefault(method => method.Name == name);

	// Task<IReadOnlyList<long>> rather than Task`1
	public static string DescribeType(Type type) => type.IsGenericType
		? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(DescribeType))}>"
		: Type.GetTypeCode(type) switch
		{
			TypeCode.Boolean => "bool",
			TypeCode.Int32 => "int",
			TypeCode.Int64 => "long",
			TypeCode.String => "string",
			_ => type == typeof(void) ? "void" : type.Name,
		};

	// The compiler names a lambda <ContainingMethod>b__12_0
	static string DescribeMethod(string name) => name.StartsWith('<') && name.IndexOf('>') is > 1 and var end
		? $"an async lambda in {name[1..end]}()"
		: $"{name}()";

	static IEnumerable<Type> GetTypeAndNestedTypes(Type type) =>
		type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(GetTypeAndNestedTypes).Prepend(type);

	static void AddCalledMethods(MethodBase method, List<string> calledMethods)
	{
		if (method.GetMethodBody()?.GetILAsByteArray() is not { } il)
			return;

		var offset = 0;

		while (offset < il.Length)
		{
			// Two byte opcodes start with 0xFE
			short value = il[offset++];

			if (value is 0xFE && offset < il.Length)
				value = unchecked((short)(0xFE00 | il[offset++]));

			if (!_opCodes.TryGetValue(value, out var opCode))
				return;

			if (opCode.OperandType is OperandType.InlineMethod && ResolveMethod(method, BitConverter.ToInt32(il, offset)) is { } calledMethod)
				calledMethods.Add(calledMethod.DeclaringType is { } declaringType ? $"{declaringType.Name}.{calledMethod.Name}" : calledMethod.Name);

			offset += opCode.OperandType switch
			{
				OperandType.InlineNone => 0,
				OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
				OperandType.InlineVar => 2,
				OperandType.InlineI8 or OperandType.InlineR => 8,
				OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, offset) * 4),
				_ => 4,
			};
		}
	}

	static MethodBase? ResolveMethod(MethodBase method, int metadataToken)
	{
		var declaringType = method.DeclaringType;

		try
		{
			return method.Module.ResolveMethod(
				metadataToken,
				declaringType is { IsGenericType: true } ? declaringType.GetGenericArguments() : null,
				method.IsGenericMethod ? method.GetGenericArguments() : null);
		}
		catch (ArgumentException)
		{
			// A call that cannot be resolved outside its generic context is not one a step looks for
			return null;
		}
	}
}