using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using static Realmar.DataBindings.Editor.Exceptions.YeetHelpers;

namespace Realmar.DataBindings.Editor.Emitting
{
	internal static class EmitHelpers
	{
		internal static void EmitCustomAttribute<TAttribute>(
			ICustomAttributeProvider target,
			ModuleDefinition module,
			params object[] ctorArgs)
			where TAttribute : Attribute
		{
			YeetIfNull(target, nameof(target));
			YeetIfNull(module, nameof(module));

			var attributeType = typeof(TAttribute);
			var args = ctorArgs.Select(arg => (Type: arg.GetType(), Value: arg)).ToArray();
			var argTypes = args.Select(tuple => tuple.Type).ToArray();

			var attributeConstructor =
				module.ImportReference(attributeType.GetConstructor(argTypes));
			var attribute = new CustomAttribute(attributeConstructor);
			target.CustomAttributes.Add(attribute);

			foreach (var (type, value) in args)
			{
				attribute.ConstructorArguments.Add(
					new CustomAttributeArgument(module.ImportReference(type).Resolve(), value));
			}
		}

		internal static Instruction GetLoadFromFieldOrCallableInstruction(IMemberDefinition bindingTarget, ILProcessor ilProcessor)
		{
			YeetIfNull(bindingTarget, nameof(bindingTarget));
			YeetIfNull(ilProcessor, nameof(ilProcessor));

			switch (bindingTarget)
			{
				case FieldDefinition field:
					return ilProcessor.Create(OpCodes.Ldfld, field);
				case MethodDefinition method:
					return ilProcessor.Create(GetCallInstruction(method), method);
				default:
					throw new NotSupportedException("BindingTarget can only be field or method");
			}
		}

		internal static OpCode GetCallInstruction(MethodDefinition method)
		{
			YeetIfNull(method, nameof(method));

			var isVirtual = method.IsVirtual || method.IsAbstract;
			return isVirtual ? OpCodes.Callvirt : OpCodes.Call;
		}

		internal static List<Instruction> GetInstructionsReferencing(Instruction searchInstruction, Collection<Instruction> instructions)
		{
			YeetIfNull(searchInstruction, nameof(searchInstruction));
			YeetIfNull(instructions, nameof(instructions));

			var found = new List<Instruction>();

			foreach (var instruction in instructions)
			{
				if (ReferenceEquals(instruction.Operand, searchInstruction))
				{
					found.Add(instruction);
				}
			}

			return found;
		}

		internal static string GetBackingFieldName(string normalName) => $"\u003C{normalName}\u003Ek__BackingField";
		internal static string GetGetterName(string normalName) => $"get_{normalName}";
		internal static string GetSetterName(string normalName) => $"set_{normalName}";
	}
}