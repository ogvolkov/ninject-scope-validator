# ninject-scope-validator
## Validate your NInject configuration for scope/lifetime errors

Sometimes dependency injection can go astray if a host service has a longer lifetime than its dependency.

For example, imagine if a singleton depends on a per-request dependency. Then such singleton always uses the same instance of a dependency which has been instantiated only once and never re-created, even though developer's idea was to create a separate instance per each web request. DI guru Mark Seemann calls this situation a [captive dependency](http://blog.ploeh.dk/2014/06/02/captive-dependency/).

NInject scope validator will validate NInject kernel bindings to find captive dependency problems. Call it after the registration of all services, providing a predicate which would tell which scope combination is deemed invalid. For example:

```
var validator = new NInjectScopeValidator(kernel,
    // check that no transient service is captured by the longer-lived service
    (scope, dependencyScope) => scope != null && dependencyScope == null
);
validator.Validate();
```
or
```
var validator = new NInjectScopeValidation.NInjectScopeValidator(kernel,
    // check that no request-scoped service is captured by the singleton
    (scope, dependencyScope) => scope is IKernel && dependencyScope is HttpContext
);
validator.Validate();
```

Validate method will throw an exception describing which service bindings are at fault, if that's the case.

This is a proof of concept. It appears to be useful, but there is no guarantee it will work under all circumstances.