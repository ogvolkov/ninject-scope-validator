using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ninject;
using Ninject.Activation;
using Ninject.Activation.Caching;
using Ninject.Infrastructure;
using Ninject.Parameters;
using Ninject.Planning;
using Ninject.Planning.Bindings;
using Ninject.Planning.Bindings.Resolvers;
using Ninject.Planning.Directives;
using Ninject.Planning.Targets;

namespace NInjectScopeValidation
{
	public delegate bool InvalidDependencyScopePredicate(object parentScope, object dependencyScope);

	public class NInjectScopeValidator
	{
		private readonly IKernel _kernel;

		private readonly InvalidDependencyScopePredicate _invalidDependencyScopePredicate;

		public NInjectScopeValidator(IKernel kernel, InvalidDependencyScopePredicate invalidDependencyScopePredicate)
		{
			_kernel = kernel;
			_invalidDependencyScopePredicate = invalidDependencyScopePredicate;
		}

		public void Validate()
		{
			var context = CreateContext();

			var allBindings = GetAllBindings(context);

			var registeredServices = BuildDependencyMap(allBindings, context);

			var violations = FindViolations(registeredServices, context);

			if (violations.Count > 0)
			{
				throw new InvalidScopeException(violations);
			}
		}

		private IReadOnlyCollection<Violation> FindViolations(Dictionary<Type, RegisteredService> registeredServices, Context context)
		{
			var result = new List<Violation>();

			foreach (KeyValuePair<Type, RegisteredService> kv in registeredServices)
			{
				var serviceType = kv.Key;
				var service = kv.Value;
				var scope = service.Scope;

				foreach (Type dependencyType in service.DependencyTypes)
				{
					object dependencyScope;
					if (registeredServices.TryGetValue(dependencyType, out RegisteredService dependencyService))
					{
						dependencyScope = dependencyService.Scope;
					}
					else
					{
						dependencyScope = _kernel.Settings.DefaultScopeCallback?.Invoke(context);
					}

					if (_invalidDependencyScopePredicate(scope, dependencyScope))
					{
						result.Add(new Violation(serviceType, dependencyType, scope, dependencyScope));
					}
				}
			}

			return result;
		}

		private Dictionary<Type, RegisteredService> BuildDependencyMap(IDictionary<Type, ICollection<IBinding>> allBindings, Context context)
		{
			var registeredServices = new Dictionary<Type, RegisteredService>();

			foreach (var kv in allBindings)
			{
				var type = kv.Key;

				if (IsIgnoredServiceType(type))
				{
					continue;
				}

				var bindings = kv.Value;

				foreach (IBinding typeBinding in bindings)
				{
					var scope = typeBinding.GetScope(context);

					var dependencies = new HashSet<Type>();
					var plan = context.Planner.GetPlan(type);

					foreach (ConstructorInjectionDirective injectionDirective in plan.GetAll<ConstructorInjectionDirective>())
					{
						foreach (ITarget target in injectionDirective.Targets)
						{
							dependencies.Add(target.Type);
						}
					}

					// TODO multiple bindings
					registeredServices[type] = new RegisteredService(scope, dependencies);
				}
			}

			return registeredServices;
		}

		private bool IsIgnoredServiceType(Type type)
		{
			// TODO is it NInject factory? investigate
			if (type.FullName.StartsWith("System.Func"))
			{
				// Deal with them another way, as GetPlan throws InvalidProgramException
				return true;
			}

			return false;
		}

		private Context CreateContext()
		{
			var request = _kernel.CreateRequest(typeof(IKernel), null, Enumerable.Empty<IParameter>(), false, true);

			var binding = _kernel.GetBindings(typeof(IKernel)).First();

			return new Context(_kernel,
				request,
				binding,
				_kernel.Components.Get<ICache>(),
				_kernel.Components.Get<IPlanner>(),
				_kernel.Components.Get<IPipeline>()
			);
		}

		private IDictionary<Type, ICollection<IBinding>> GetAllBindings(Context context)
		{
			// kernel does not expose all bindings, so we use a custom binding resolver to get them,
			// as suggested in https://stackoverflow.com/a/3783203/76176
			_kernel.Components.Add<IBindingResolver, TrackingBindingResolver>();
			TrackingBindingResolver.Context = context;

			// we need to run a single out of cache resolution to make custom resolver work
			// use a dummy class to ensure it's well known to us but not cached
			_kernel.GetBindings(typeof(NotBoundDummy));

			_kernel.Components.Remove<IBindingResolver, TrackingBindingResolver>();

			return TrackingBindingResolver.Bindings;
		}

		private class TrackingBindingResolver : IBindingResolver
		{
			public static Context Context { get; set; }

			public static IDictionary<Type, ICollection<IBinding>> Bindings { get; private set; }

			public INinjectSettings Settings { get; set; }


			public IEnumerable<IBinding> Resolve(Multimap<Type, IBinding> bindings, Type service)
			{
				Bindings = bindings.ToDictionary(it => it.Key, it => it.Value);

				return Enumerable.Empty<IBinding>();
			}

			public void Dispose()
			{
			}
		}

		private class NotBoundDummy
		{
		}

		private class RegisteredService
		{
			public object Scope { get; }

			public ISet<Type> DependencyTypes { get; }

			public RegisteredService(object scope, ISet<Type> dependencyTypes)
			{
				Scope = scope;
				DependencyTypes = dependencyTypes;
			}
		}
	}

	public class Violation
	{
		public Type ServiceType { get; }

		public Type DependencyType { get; }

		public object ServiceScope { get; }

		public object DependencyScope { get; }

		public Violation(Type serviceType, Type dependencyType, object serviceScope, object dependencyScope)
		{
			ServiceType = serviceType;
			DependencyType = dependencyType;
			ServiceScope = serviceScope;
			DependencyScope = dependencyScope;
		}
	}

	public class InvalidScopeException : Exception
	{
		public IReadOnlyCollection<Violation> Violations { get; }

		public InvalidScopeException(IReadOnlyCollection<Violation> violations)
			: base(BuildMessage(violations))
		{
			Violations = violations;
		}

		private static string BuildMessage(IReadOnlyCollection<Violation> violations)
		{
			var builder = new StringBuilder();

			builder.AppendLine("Combination of service and dependency scope is not valid");

			foreach (var violation in violations)
			{
				builder.AppendLine($"Service {violation.ServiceType} with scope {violation.ServiceScope ?? "transient"} depends on {violation.DependencyType} with scope {violation.DependencyScope ?? "transient"}");
			}

			return builder.ToString();
		}
	}
}
