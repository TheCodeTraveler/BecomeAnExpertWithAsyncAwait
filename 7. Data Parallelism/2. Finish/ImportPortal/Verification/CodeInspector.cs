using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace ImportPortal;

// Workshop plumbing: reads the compiled IL of a type, including its lambdas, local functions and async state machines.
// The steps use it only for requirements with nothing to observe from outside, such as a PLINQ query that finishes in a millisecond.
public static class CodeInspector
{
	const BindingFlags _declaredMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

	static readonly Dictionary<short, OpCode> _opCodes = typeof(OpCodes)
		.GetFields(BindingFlags.Public | BindingFlags.Static)
		.Select(static field => (OpCode)(field.GetValue(null) ?? throw new InvalidOperationException($"OpCodes.{field.Name} has no value")))
		.ToDictionary(static opCode => opCode.Value);

	// Every method the type calls, creates a delegate for, or constructs, named "DeclaringType.Name", for example "ParallelEnumerable.AsParallel"
	public static IReadOnlySet<string> GetCalledMethods(Type type)
	{
		var calledMethods = new HashSet<string>(StringComparer.Ordinal);

		foreach (var method in GetMethodsAndConstructors(type))
		{
			AddCalledMethods(method, calledMethods);
		}

		return calledMethods;
	}

	// Lambdas, local functions and methods that are async void. Nothing can await one, and an exception inside one ends the app.
	public static IReadOnlyList<MethodInfo> FindAsyncVoidMethods(Type type) =>
	[
		.. GetTypeAndNestedTypes(type)
			.SelectMany(static declaringType => declaringType.GetMethods(_declaredMembers))
			.Where(static method => method.ReturnType == typeof(void) && method.IsDefined(typeof(AsyncStateMachineAttribute), inherit: false)),
	];

	static IEnumerable<MethodBase> GetMethodsAndConstructors(Type type) => GetTypeAndNestedTypes(type)
		.SelectMany(static declaringType => declaringType.GetMethods(_declaredMembers).Cast<MethodBase>().Concat(declaringType.GetConstructors(_declaredMembers)));

	// The compiler puts closures, lambdas and async state machines in nested types, sometimes nested more than one level deep
	static IEnumerable<Type> GetTypeAndNestedTypes(Type type) =>
		[type, .. type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(GetTypeAndNestedTypes)];

	static void AddCalledMethods(MethodBase method, HashSet<string> calledMethods)
	{
		var il = method.GetMethodBody()?.GetILAsByteArray();

		if (il is null)
			return;

		var typeArguments = method.DeclaringType is { IsGenericType: true } declaringType ? declaringType.GetGenericArguments() : null;
		var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

		var position = 0;

		while (position < il.Length)
		{
			// Two-byte opcodes start with 0xFE
			var value = il[position] is 0xFE && position + 1 < il.Length
				? unchecked((short)(0xFE00 | il[++position]))
				: il[position];

			position++;

			if (!_opCodes.TryGetValue(value, out var opCode))
				return;

			if (opCode.OperandType is OperandType.InlineMethod && position + 4 <= il.Length)
			{
				var token = BitConverter.ToInt32(il, position);

				try
				{
					if (method.Module.ResolveMethod(token, typeArguments, methodArguments) is { DeclaringType: { } calledType } calledMethod)
						calledMethods.Add($"{calledType.Name}.{calledMethod.Name}");
				}
				catch (ArgumentException)
				{
					// A token this module cannot resolve is not a call the steps look for
				}
			}

			position += GetOperandSize(opCode, il, position);
		}
	}

	static int GetOperandSize(OpCode opCode, byte[] il, int position) => opCode.OperandType switch
	{
		OperandType.InlineNone => 0,
		OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
		OperandType.InlineVar => 2,
		OperandType.InlineI8 or OperandType.InlineR => 8,
		OperandType.InlineSwitch when position + 4 <= il.Length => 4 + (BitConverter.ToInt32(il, position) * 4),
		_ => 4,
	};
}