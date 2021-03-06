using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Realmar.DataBindings.Editor.BCL.System;
using Realmar.DataBindings.Editor.Cecil;
using Realmar.DataBindings.Editor.Emitting;
using Realmar.DataBindings.Editor.Exceptions;
using Realmar.DataBindings.Editor.IoC;
using Realmar.DataBindings.Editor.Shared.Extensions;
using static Realmar.DataBindings.Editor.Exceptions.YeetHelpers;
using static Realmar.DataBindings.Editor.Shared.SharedHelpers;
using static Realmar.DataBindings.Editor.Weaving.WeaveHelpers;

namespace Realmar.DataBindings.Editor.Weaving
{
	internal class Weaver
	{
		/*
		 * We want to have following scenario:
		 *
		 * View --> ViewModel --> Model
		 * View <--> ViewModel --> Model
		 *
		 * View <-- ViewModel <-- Model
		 * View <--> ViewModel <-- Model
		 *
		 * View --> ViewModel <--> Model
		 * View <--> ViewModel <--> Model
		 *
		 * Problem is that when always using set helpers (instead of property setter) only
		 * we cannot do bindings across 3 objects because the sh do not contain
		 * the woven bindings from the actual property setter.
		 *
		 * If we would always use the property setter, then there would be a stack overflow
		 * when doing TwoWay bindings, because the setter would call each other.
		 *
		 * If we call sh in OneWay binding and property setter in FromTarget binding then
		 * we end up calling also the Model sh instead of just the ViewModel and View.
		 * eg.:
		 * Model uses VM setter --> VM setter calls Model sh <-- this call is unnecessary because data comes from Model, we dont need to set it again
		 *						--> VM setter calls View sh
		 *
		 * To solve this problem we introduce a special set helper which contains all
		 * the woven bindings from the property setter except the one which points to
		 * the Model.
		 *
		 * ---------------------------------------------------------------------------------------
		 *
		 * we need to weave special set helpers for FromTargetBindings/TwoWay bindings
		 * this special set helper should have all the woven bindings from the property setter
		 * except the one to itself.
		 *
		 * A has TwoWay binding to B
		 * A <--> B
		 *
		 * OneWay bindings
		 * A --> C
		 * A --> D
		 *
		 * A setter has following calls:
		 * A --> B
		 * A --> C
		 * A --> D
		 *
		 * A sh has none of those calls
		 *
		 * A uses sh to B
		 * B uses special sh to A which includes all other bindings from the A setter
		 *
		 * A special sh has following calls
		 * A --> C
		 * A --> D
		 *
		 * ---------------------------------------------------------------------------------------
		 *
		 * Instead of differentiating between set helper and special set helper we could
		 * tread all set helpers as special set helpers. Meaning that the sh is specific to the
		 * data source.
		 * This will probably increase the DLL size significantly as there will be a lot of
		 * duplicate code in all those set helpers.
		 *
		 * Well, we probably need to do this because even with OneWay bindings we need
		 * the woven bindings from the property setter in order to populate the data
		 * further.
		 *
		 * --> special set helpers only it is then.
		 */

		#region Classes

		private readonly struct BindingCommand
		{
			internal EmitBindingCommand Command { get; }
			internal MethodDefinition ToSetter { get; }

			internal BindingCommand(EmitBindingCommand command, MethodDefinition property)
			{
				Command = command;
				ToSetter = property;
			}

			internal void Deconstruct(out EmitBindingCommand command, out MethodDefinition toProperty)
			{
				command = Command;
				toProperty = ToSetter;
			}
		}

		#endregion

		#region Fields

		private readonly Random _random = new Random();
		private readonly Emitter _emitter = ServiceLocator.Current.Resolve<Emitter>();
		private readonly DerivativeResolver _derivativeResolver = ServiceLocator.Current.Resolve<DerivativeResolver>();
		private readonly HashSet<int> _wovenBindings = new HashSet<int>();
		private readonly Dictionary<MethodDefinition, List<BindingCommand>> _bindingsForProperty = new Dictionary<MethodDefinition, List<BindingCommand>>();
		private readonly Dictionary<MethodDefinition, HashSet<MethodDefinition>> _propertySettingOtherProperties = new Dictionary<MethodDefinition, HashSet<MethodDefinition>>();
		private readonly HashSet<TypeDefinition> _alreadyCreatedMethodMementos = new HashSet<TypeDefinition>();
		private readonly Dictionary<MethodDefinition, MethodMemento> _originalSetters = new Dictionary<MethodDefinition, MethodMemento>();
		private readonly Dictionary<(MethodDefinition From, MethodDefinition To), MethodDefinition> _setHelpers = new Dictionary<(MethodDefinition From, MethodDefinition To), MethodDefinition>();
		private readonly Dictionary<(TypeDefinition WeavedType, TypeReference ConverterType), FieldDefinition> _converterFields = new Dictionary<(TypeDefinition WeavedType, TypeReference ConverterType), FieldDefinition>();

		#endregion

		internal void Weave(in WeaveMethodParameters parameters, ParameterDefinition fromParameter)
		{
			var fromSetter = parameters.FromSetter;
			var parameterIndex = fromSetter.Parameters.IndexOf(fromParameter);

			if (parameterIndex < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(fromParameter), $"Cannot find parameter {fromParameter.Name} on method {fromSetter.FullName}");
			}

			Weave(parameters, (ushort) parameterIndex);
		}

		internal void Weave(in WeaveMethodParameters parameters, MethodDefinition fromGetter)
		{
			Weave(parameters, (object) fromGetter);
		}

		private void Weave<TFromGetter>(in WeaveMethodParameters parameters, TFromGetter fromGetter)
		{
			var fromPropertyDeclaringType = parameters.FromSetter.DeclaringType;
			YeetIfInaccessible(parameters.ToSetter, fromPropertyDeclaringType);

			if (parameters.BindingTarget != null)
			{
				YeetIfInaccessible(parameters.BindingTarget, fromPropertyDeclaringType);
			}

			CreateMethodMemento(parameters.ToSetter.DeclaringType);
			CreateMethodMemento(parameters.FromSetter.DeclaringType);

			if (parameters.FromSetter.IsVirtual == false)
			{
				WeaveNonAbstractBinding(parameters, fromGetter);
			}
			else
			{
				WeaveBindingInHierarchy(parameters, fromGetter);
			}
		}

		internal void Weave(in WeaveParameters parameters)
		{
			Weave(
				new WeaveMethodParameters(
					fromSetter: parameters.FromProperty.GetSetMethodOrYeet(),
					toSetter: parameters.ToProperty.GetSetMethodOrYeet(),
					bindingTarget: parameters.BindingTarget,
					emitNullCheck: parameters.EmitNullCheck,
					converter: parameters.Converter
				),
				parameters.FromProperty.GetGetMethodOrYeet());
		}

		internal PropertyDefinition WeaveTargetToSourceAccessorCommand(in AccessorSymbolParameters parameters)
		{
			var accessorSymbol = GetAccessorPropertyInHierarchy(parameters.SourceType, parameters.TargetType);
			if (accessorSymbol == null)
			{
				// WEAVE ACCESSOR METHOD
				accessorSymbol = _emitter.EmitAccessor(parameters.TargetType, parameters.SourceType, false);
				if (parameters.TargetType.IsInterface)
				{
					var list = _derivativeResolver.GetDirectlyDerivedTypes(parameters.TargetType);
					foreach (var derivedType in list)
					{
						if (GetAccessorProperty(parameters.SourceType, derivedType) == null)
						{
							_emitter.EmitAccessor(derivedType, parameters.SourceType, true);
						}
					}
				}
			}

			var accessorSymbolSetter = accessorSymbol.SetMethod;
			if (parameters.BindingInitializer.IsAbstract)
			{
				WeaveAbstractAccessorInitialization(accessorSymbolSetter, parameters);
			}
			else
			{
				_emitter.EmitAccessorInitialization(accessorSymbolSetter, parameters.BindingInitializer, parameters.BindingTarget, parameters.Settings.ThrowOnFailure);
			}

			return accessorSymbol;
		}

		private void CreateMethodMemento(TypeDefinition type)
		{
			if (_alreadyCreatedMethodMementos.Contains(type) == false)
			{
				var derivedProperties = _derivativeResolver
					.GetDerivedTypes(type)
					.SelectMany(definition => definition.Properties);

				var setters = type
					.GetPropertiesInBaseHierarchy()
					.Concat(derivedProperties)
					.Where(definition => definition.DeclaringType.Module.Assembly.IsSame(type.Module.Assembly))
					.Select(definition => definition.SetMethod)
					.WhereNotNull()
					.Where(definition => definition.IsAbstract == false);


				foreach (var setter in setters)
				{
					if (_originalSetters.ContainsKey(setter) == false)
					{
						_originalSetters[setter] = _emitter.CreateMethodMemento(setter);
					}
				}

				_alreadyCreatedMethodMementos.Add(type);
			}
		}

		private MethodDefinition WeaveSetHelper(MethodDefinition fromSetter, MethodDefinition toSetter)
		{
			MethodDefinition result = null;

			var key = (fromSetter, toSetter);
			if (_setHelpers.ContainsKey(key))
			{
				result = _setHelpers[key];
			}
			else
			{
				var setHelperName = $"From_{FormatSetterName(fromSetter)}_To_{toSetter.DeclaringType.Name}_With_{FormatSetterName(toSetter)}_{_random.Next()}";

				if (toSetter.IsVirtual || toSetter.IsAbstract)
				{
					foreach (var (derivedToSetter, setHelper) in WeaveSetHelperRecursive(fromSetter, toSetter, setHelperName))
					{
						_setHelpers[(fromSetter, derivedToSetter)] = setHelper;

						if (derivedToSetter == toSetter)
						{
							result = setHelper;
						}
					}
				}
				else
				{
					result = WeaveSetHelper(fromSetter, toSetter, setHelperName);
					_setHelpers[key] = result;
				}

				if (result == null)
				{
					throw new BigOOFException($"Weaving set helper failed name {setHelperName} from {fromSetter} to {toSetter}");
				}
			}

			return result;
		}

		private MethodDefinition WeaveSetHelper(MethodDefinition fromSetter, MethodDefinition toSetter, string name)
		{
			MethodDefinition setHelper;
			if (_originalSetters.ContainsKey(toSetter) == false)
			{
				if (toSetter.IsAbstract == false)
				{
					throw new BigOOFException($"Could not find original setter for non-abstract {toSetter.FullName}");
				}
				else
				{
					// If we did not find any original setter, then it's because the setter is abstract,
					// so we don't need to weave the method body (using a MethodMemento)
					setHelper = _emitter.EmitSetHelper(name, toSetter);
				}
			}
			else
			{
				setHelper = _emitter.EmitSetHelper(name, toSetter, _originalSetters[toSetter]);
				if (_bindingsForProperty.ContainsKey(toSetter))
				{
					// apply existing bindings
					var bindings = _bindingsForProperty[toSetter];
					foreach (var (command, targetProperty) in bindings)
					{
						if (targetProperty != fromSetter)
						{
							command.Emit(setHelper);
						}
					}
				}
			}

			return setHelper;
		}

		private IEnumerable<(MethodDefinition Source, MethodDefinition Helper)> WeaveSetHelperRecursive(MethodDefinition fromSetter, MethodDefinition toSetter, string name)
		{
			var toType = toSetter.DeclaringType;
			var derivedToSetters = _derivativeResolver
				.GetDerivedTypes(toType)
				.SelectMany(definition => definition.GetMethods())
				.Concat(toType.GetMethodsInBaseHierarchy())
				.WhereNotNull()
				.Where(definition => definition.Name == toSetter.Name)
				.Where(definition => definition.DeclaringType.Module.Assembly.IsSame(toType.Module.Assembly))
				.Distinct()
				.ToArray();

			foreach (var derivedToSetter in derivedToSetters)
			{
				yield return (derivedToSetter, WeaveSetHelper(fromSetter, derivedToSetter, name));
			}
		}

		private Converter WeaveConverter(TypeReference converterType, MethodDefinition fromSetter, MethodDefinition toSetter)
		{
			var converterTypeDefinition = converterType.Resolve();
			var isConverter = converterTypeDefinition.Interfaces
				.Any(implementation => implementation.InterfaceType.Name.StartsWith("IValueConverter"));

			if (isConverter == false)
			{
				throw new NotAConverterException(converterType, fromSetter);
			}

			if (converterTypeDefinition.IsAbstract)
			{
				throw new AbstractConverterException(converterType, fromSetter);
			}

			if (converterType.IsGenericInstance == false && converterType.HasGenericParameters)
			{
				throw new OpenGenericConverterNotSupported(converterType, fromSetter);
			}

			var ctor = converterTypeDefinition.GetConstructors().FirstOrDefault(definition => definition.HasParameters == false);
			if (ctor == null)
			{
				throw new MissingDefaultCtorException(converterType.GetActualType());
			}

			var module = fromSetter.DeclaringType.Module;
			var fromType = fromSetter.Parameters[0].ParameterType.Resolve();
			var toType = toSetter.Parameters[0].ParameterType.Resolve();

			MethodReference convertMethod = null;
			if (converterType.IsGenericInstance)
			{
				var genericConverterType = (GenericInstanceType) converterType;
				var genericConstrainedFromType = genericConverterType.GenericArguments[0].Resolve();
				var genericConstrainedToType = genericConverterType.GenericArguments[1].Resolve();

				foreach (var definition in converterTypeDefinition.GetMethods())
				{
					var argumentType = definition.Parameters[0].ParameterType;
					var returnType = definition.ReturnType;
					var actualArgumentType = argumentType.Name == "TFrom" ? genericConstrainedFromType : genericConstrainedToType;
					var actualReturnType = returnType.Name == "TTo" ? genericConstrainedToType : genericConstrainedFromType;

					if (actualArgumentType == fromType && actualReturnType == toType)
					{
						convertMethod = definition.CreateReference(converterType);
						break;
					}
				}
			}
			else
			{
				convertMethod = converterTypeDefinition.GetMethods().FirstOrDefault(definition =>
					definition.Parameters[0].ParameterType.Resolve() == fromType &&
					definition.ReturnType.Resolve() == toType);
			}

			if (convertMethod == null)
			{
				throw new MismatchingConverterTypesException(converterType, fromSetter);
			}

			convertMethod = module.ImportReference(convertMethod);

			var key = (fromSetter.DeclaringType, converterType);
			if (_converterFields.TryGetValue(key, out var converterField) == false)
			{
				var importedConverterType = module.ImportReference(converterType);
				var importedCtor = module.ImportReference(ctor.CreateReference(converterType));

				var converterFieldName = $"_converter_{SanitizeName(converterType.Name)}_{_random.Next()}";
				converterField = _emitter.EmitConverter(importedConverterType, importedCtor, fromSetter.DeclaringType, converterFieldName);
			}

			return new Converter(converterField, convertMethod);
		}

		private void WeaveNonAbstractBinding<TFromGetter>(in WeaveMethodParameters parameters, TFromGetter fromGetter)
		{
			var wovenBindingsHash = GetHashCode(parameters, fromGetter);
			if (_wovenBindings.Contains(wovenBindingsHash) == false)
			{
				MethodDefinition toSetHelper;
				if (BelongsToProperty(parameters.ToSetter))
				{
					// This means that its a ToMethodBinding and thus does not require a set helper.
					toSetHelper = WeaveSetHelper(parameters.FromSetter, parameters.ToSetter);
				}
				else
				{
					toSetHelper = parameters.ToSetter;
				}

				_wovenBindings.Add(wovenBindingsHash);

				Converter converter = default;
				if (parameters.Converter != null)
				{
					converter = WeaveConverter(parameters.Converter, parameters.FromSetter, parameters.ToSetter);
				}

				var emitCommand = CreateEmitCommand(new EmitParameters(parameters.BindingTarget, parameters.FromSetter, toSetHelper, parameters.EmitNullCheck, converter), fromGetter);
				emitCommand.Emit(parameters.FromSetter);

				if (_bindingsForProperty.TryGetValue(parameters.FromSetter, out var commands) == false)
				{
					commands = new List<BindingCommand>();
					_bindingsForProperty[parameters.FromSetter] = commands;
				}

				commands.Add(new BindingCommand(emitCommand, parameters.ToSetter));

				foreach (var (origin, destinations) in _propertySettingOtherProperties)
				{
					if (destinations.Contains(parameters.FromSetter) && parameters.ToSetter != origin)
					{
						var setHelper = _setHelpers[(origin, parameters.FromSetter)];
						emitCommand.Emit(setHelper);
					}
				}

				if (_propertySettingOtherProperties.TryGetValue(parameters.FromSetter, out var references) == false)
				{
					references = new HashSet<MethodDefinition>();
					_propertySettingOtherProperties[parameters.FromSetter] = references;
				}

				references.Add(parameters.ToSetter);

				/*
				 * v to vm via sh
				 * vm to v via sh
				 *
				 * vm to m via sh | v to vm?
				 * m to vm via sh
				 *
				 * v to vm via s --> vm to v via sh | vm to v is unnecessary op
				 *				 --> vm to m via sh
				 *
				 * m to vm via s --> vm to m via sh | vm to m is unnecessary op
				 *				 --> vm to v via sh
				 *
				 * --> TwoWay: in vm you want to have a setter which contains all bindings except the one to the data source
				 *			   when adding new bindings, those special setters need to be kept up to date
				 *
				 */
			}
		}

		private EmitBindingCommand CreateEmitCommand<TFromGetter>(in EmitParameters parameters, TFromGetter rawFromGetter)
		{
			switch (rawFromGetter)
			{
				case ushort index:
					return _emitter.CreateEmitCommand(parameters, index);

				case MethodDefinition fromGetter:
					return _emitter.CreateEmitCommand(parameters, fromGetter);
				default:
					throw new NotSupportedException($"{rawFromGetter.GetType()} is not a valid FromGetter type");
			}
		}

		private void WeaveBindingInHierarchy<TFromGetter>(in WeaveMethodParameters parameters, TFromGetter fromGetter)
		{
			var foundNonAbstract = false;
			var fromSetterName = parameters.FromSetter.Name;
			var derivedTypes = _derivativeResolver.GetDerivedTypes(parameters.FromSetter.DeclaringType);

			foreach (var typeDefinition in derivedTypes)
			{
				var derivedFromSetter = typeDefinition.GetMethod(fromSetterName);
				if (derivedFromSetter != null && derivedFromSetter.IsAbstract == false)
				{
					WeaveNonAbstractBinding(parameters.UsingFromSetter(derivedFromSetter), fromGetter);
					foundNonAbstract = true;
				}
			}

			if (foundNonAbstract == false)
			{
				throw new MissingNonAbstractSymbolException(parameters.FromSetter.FullName);
			}
		}

		private int GetHashCode<TFromGetter>(in WeaveMethodParameters parameters, TFromGetter fromGetter)
		{
			return HashCode.Combine(fromGetter, parameters.FromSetter, parameters.ToSetter, parameters.BindingTarget);
		}

		private void WeaveAbstractAccessorInitialization(MethodDefinition accessorSymbol, in AccessorSymbolParameters parameters)
		{
			var bindingInitializer = parameters.BindingInitializer;
			var concretes = GetNonAbstractMethods(bindingInitializer);
			var found = false;

			foreach (var concrete in concretes)
			{
				found = true;
				_emitter.EmitAccessorInitialization(accessorSymbol, concrete, parameters.BindingTarget, parameters.Settings.ThrowOnFailure);
			}

			if (found == false)
			{
				throw new MissingNonAbstractBindingInitializer(bindingInitializer.FullName);
			}
		}

		private IEnumerable<MethodDefinition> GetNonAbstractMethods(MethodDefinition method)
		{
			if (method == null)
			{
				return Enumerable.Empty<MethodDefinition>();
			}

			return GetNonAbstractMethods(method.Name, method.DeclaringType);
		}

		private IEnumerable<MethodDefinition> GetNonAbstractMethods(string methodName, TypeDefinition type)
		{
			var initializer = type.GetMethod(methodName);
			if (initializer != null)
			{
				if (initializer.IsAbstract == false)
				{
					return initializer.Yield();
				}
				else
				{
					return GetNonAbstractMethodInDerivedTypes(methodName, type);
				}
			}
			else
			{
				return GetNonAbstractMethodInDerivedTypes(methodName, type);
			}
		}

		private IEnumerable<MethodDefinition> GetNonAbstractMethodInDerivedTypes(string methodName, TypeDefinition type)
		{
			var newDerivedTypes = _derivativeResolver.GetDirectlyDerivedTypes(type);

			foreach (var derivedType in newDerivedTypes)
			{
				foreach (var nonAbstractMethod in GetNonAbstractMethods(methodName, derivedType))
				{
					yield return nonAbstractMethod;
				}
			}
		}

		private bool BelongsToProperty(MethodDefinition method)
		{
			return method.DeclaringType.Properties
				.Select(definition => definition.SetMethod)
				.WhereNotNull()
				.Any(definition => definition.Name == method.Name);
		}
	}
}
