using System;
using System.Linq;
using Mono.Cecil;
using Realmar.DataBindings.Editor.Cecil;
using Realmar.DataBindings.Editor.Exceptions;
using static Realmar.DataBindings.Editor.Exceptions.YeetHelpers;

namespace Realmar.DataBindings.Editor.Shared
{
	internal static class SharedHelpers
	{
		internal static TypeDefinition GetReturnType(IMemberDefinition bindingTarget)
		{
			YeetIfNull(bindingTarget, nameof(bindingTarget));

			switch (bindingTarget)
			{
				case FieldDefinition field:
					return field.FieldType.Resolve();
				case MethodDefinition method:
					return method.ReturnType.Resolve();
				case PropertyDefinition property:
					return property.PropertyType.Resolve();
				case EventDefinition @event:
					return @event.EventType.Resolve();
				default:
					throw new ArgumentException("Only fields, properties, events or methods can have return types.");
			}
		}

		internal static PropertyDefinition GetAccessorProperty(TypeDefinition sourceType, TypeDefinition targetType)
		{
			var injectedSourceName = GetAccessorPropertyName(sourceType);
			return targetType.GetProperty(injectedSourceName);
		}

		internal static PropertyDefinition GetAccessorPropertyInHierarchy(TypeDefinition sourceType, TypeDefinition targetType)
		{
			YeetIfNull(sourceType, nameof(sourceType));
			YeetIfNull(targetType, nameof(targetType));

			var injectedSourceName = GetAccessorPropertyName(sourceType);
			var accessorInterface = targetType.GetInInterfaces(injectedSourceName, definition => definition.Properties).FirstOrDefault();
			if (accessorInterface != null)
			{
				return accessorInterface;
			}
			else
			{
				var properties = targetType.GetPropertiesInBaseHierarchy(injectedSourceName).ToList();
				if (properties.Count == 0)
				{
					return null;
				}
				else if (properties.Count > 1)
				{
					throw new BigOOFException("FATAL ERROR: Cannot weave assembly because multiple target to source fields of the same type are found on the target");
				}
				else
				{
					return properties[0];
				}
			}
		}

		internal static string GetAccessorPropertyName(TypeDefinition sourceType)
		{
			YeetIfNull(sourceType, nameof(sourceType));
			return sourceType.FullName.Replace(".", "");
		}
	}
}
