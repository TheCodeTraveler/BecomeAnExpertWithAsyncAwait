using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace TelemetryPipeline;

// Workshop plumbing: lists the methods your compiled code calls, formatted as "Type.Method".
// The steps use it only for requirements that no behavior can show, such as reading a counter with Volatile.Read.
// It reads the IL, so it also sees calls inside lambdas, local functions and async state machines.
public static class CodeInspector
{
	const BindingFlags _declaredMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

	// Every opcode, keyed by its value. Two-byte opcodes start with 0xFE.
	static readonly Dictionary<short, OpCode> _opCodes = typeof(OpCodes)
		.GetFields(BindingFlags.Public | BindingFlags.Static)
		.Select(static field => (OpCode)(field.GetValue(null) ?? throw new InvalidOperationException($"OpCodes.{field.Name} has no value")))
		.ToDictionary(static opCode => opCode.Value);

	// Every call in a type and in the types nested inside it. A method called in two places appears twice.
	public static IReadOnlyList<string> GetCalledMethods(Type type)
	{
		var calls = new List<string>();

		AddCalls(type, calls);

		return calls;
	}

	// Every call in one method, including the async state machine the compiler generated for it
	public static IReadOnlyList<string> GetCalledMethods(MethodInfo method)
	{
		var calls = new List<string>();

		AddCalls(method, calls);

		if (method.GetCustomAttribute<StateMachineAttribute>() is { } stateMachine)
			AddCalls(stateMachine.StateMachineType, calls);

		return calls;
	}

	static void AddCalls(Type type, List<string> calls)
	{
		foreach (var method in type.GetMethods(_declaredMembers))
		{
			AddCalls(method, calls);
		}

		foreach (var constructor in type.GetConstructors(_declaredMembers))
		{
			AddCalls(constructor, calls);
		}

		foreach (var nestedType in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
		{
			AddCalls(nestedType, calls);
		}
	}

	static void AddCalls(MethodBase method, List<string> calls)
	{
		if (method.GetMethodBody()?.GetILAsByteArray() is not { } il)
			return;

		var typeArguments = method.DeclaringType is { IsGenericType: true } declaringType ? declaringType.GetGenericArguments() : null;
		var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
		var position = 0;

		while (position < il.Length)
		{
			var opCode = il[position] is 0xFE
				? _opCodes[unchecked((short)(0xFE00 | il[position + 1]))]
				: _opCodes[il[position]];

			position += opCode.Size;

			if (opCode.OperandType is OperandType.InlineMethod)
			{
				try
				{
					var token = BitConverter.ToInt32(il, position);

					if (method.Module.ResolveMethod(token, typeArguments, methodArguments) is { DeclaringType: { } calledType } calledMethod)
						calls.Add($"{calledType.Name}.{calledMethod.Name}");
				}
				catch (ArgumentException)
				{
					// A token this module cannot resolve is not a call to anything the steps look for
				}
			}

			position += opCode.OperandType switch
			{
				OperandType.InlineNone => 0,
				OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
				OperandType.InlineVar => 2,
				OperandType.InlineI8 or OperandType.InlineR => 8,
				OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, position) * 4),
				_ => 4,
			};
		}
	}
}