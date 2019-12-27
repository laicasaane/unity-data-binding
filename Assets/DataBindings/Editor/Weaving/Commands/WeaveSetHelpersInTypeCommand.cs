using Mono.Cecil;
using Realmar.DataBindings.Editor.Commands;
using Realmar.DataBindings.Editor.Extensions;
using System.Linq;

namespace Realmar.DataBindings.Editor.Weaving.Commands
{
	internal class WeaveSetHelpersInTypeCommand : BaseCommand
	{
		private WeaveSetHelpersInTypeCommand()
		{
		}

		internal static ICommand Create(TypeDefinition type)
		{
			var command = new WeaveSetHelpersInTypeCommand();
			var setMethods = type
				.GetPropertiesInBaseHierarchy()
				.Where(definition => definition.DeclaringType.Module.Assembly.IsSame(type.Module.Assembly))
				.Select(definition => definition.SetMethod)
				.WhereNotNull().ToArray();

			foreach (var setMethod in setMethods)
			{
				if (setMethod.IsVirtual || setMethod.IsAbstract)
				{
					command.AddChild(WeaveSetHelperRecursiveCommand.Create(setMethod));
				}
				else
				{
					command.AddChild(WeaveSetHelperCommand.Create(setMethod));
				}
			}

			return command;
		}
	}
}
