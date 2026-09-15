using System.Reflection;
using System.Reflection.Emit;

namespace StockWatch;

// Workshop plumbing: lists the methods a member of a type calls, including inside its lambdas and async state machines.
// A few requirements have nothing a check can observe from outside, such as Volatile.Read in a getter, so those checks read your compiled code instead.
public static class CodeInspector
{
	const BindingFlags _declaredMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

	// Every IL opcode by its value, so the method body can be walked one instruction at a time
	static readonly Dictionary<short, OpCode> _opCodes = typeof(OpCodes)
		.GetFields(BindingFlags.Public | BindingFlags.Static)
		.Select(static field => field.GetValue(null))
		.OfType<OpCode>()
		.ToDictionary(static opCode => opCode.Value);

	// Returns names such as "Interlocked.Increment" for every call memberName makes, in Debug and in Release builds.
	// The compiler names a lambda <RefreshQuotes>b__0 and its state machine <<RefreshQuotes>b__0>d, so those count as part of RefreshQuotes.
	public static IReadOnlySet<string> GetCalledMethods(Type type, string memberName)
	{
		var calledMethods = new HashSet<string>(StringComparer.Ordinal);

		AddCalledMethods(type, memberName, false, calledMethods);

		return calledMethods;
	}

	static void AddCalledMethods(Type type, string memberName, bool isPartOfMember, HashSet<string> calledMethods)
	{
		foreach (var method in type.GetMethods(_declaredMembers).Concat<MethodBase>(type.GetConstructors(_declaredMembers)))
		{
			if (isPartOfMember || IsPartOf(method.Name, memberName))
				AddCalls(method, calledMethods);
		}

		foreach (var nestedType in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
		{
			AddCalledMethods(nestedType, memberName, isPartOfMember || IsPartOf(nestedType.Name, memberName), calledMethods);
		}
	}

	static void AddCalls(MethodBase method, HashSet<string> calledMethods)
	{
		if (method.GetMethodBody()?.GetILAsByteArray() is not { } il)
			return;

		var typeArguments = method.DeclaringType is { IsGenericType: true } declaringType ? declaringType.GetGenericArguments() : null;
		var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

		var offset = 0;

		while (offset < il.Length)
		{
			// Two-byte opcodes start with 0xFE
			var value = (short)il[offset++];

			if (value is 0xFE && offset < il.Length)
				value = unchecked((short)(0xFE00 | il[offset++]));

			if (!_opCodes.TryGetValue(value, out var opCode))
				return;

			if (opCode.OperandType is OperandType.InlineMethod)
			{
				try
				{
					if (method.Module.ResolveMethod(BitConverter.ToInt32(il, offset), typeArguments, methodArguments) is { } calledMethod)
						calledMethods.Add($"{calledMethod.DeclaringType?.Name}.{calledMethod.Name}");
				}
				catch (ArgumentException)
				{
					// A token this module cannot resolve is not a call the steps look for
				}
			}

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

	static bool IsPartOf(string name, string memberName) => name == memberName || name.Contains($"<{memberName}>", StringComparison.Ordinal);
}