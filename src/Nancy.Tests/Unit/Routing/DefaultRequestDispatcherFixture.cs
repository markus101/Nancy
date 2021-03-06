namespace Nancy.Tests.Unit.Routing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using FakeItEasy;
    using Fakes;
    using Nancy.Responses.Negotiation;
    using Nancy.Routing;
    using Xunit;
    using ResolveResult = System.Tuple<Nancy.Routing.Route, DynamicDictionary, System.Func<NancyContext, Response>, System.Action<NancyContext>>;

    public class DefaultRequestDispatcherFixture
    {
        private readonly DefaultRequestDispatcher requestDispatcher;
        private readonly IRouteResolver routeResolver;
        private readonly IRouteInvoker routeInvoker;
        private readonly IList<IResponseProcessor> responseProcessors;

        public DefaultRequestDispatcherFixture()
        {
            this.responseProcessors = new List<IResponseProcessor>();
            this.routeResolver = A.Fake<IRouteResolver>();
            this.routeInvoker = A.Fake<IRouteInvoker>();

            A.CallTo(() => this.routeInvoker.Invoke(A<Route>._, A<DynamicDictionary>._, A<NancyContext>._)).ReturnsLazily(arg =>
                {
                    return (Response)((Route)arg.Arguments[0]).Action.Invoke(arg.Arguments[1]);
                });

            this.requestDispatcher = 
                new DefaultRequestDispatcher(this.routeResolver, this.responseProcessors, this.routeInvoker);

            var resolvedRoute = new ResolveResult(
               new FakeRoute(),
               DynamicDictionary.Empty,
               null,
               null);

            A.CallTo(() => this.routeResolver.Resolve(A<NancyContext>._)).Returns(resolvedRoute);
        }

        [Fact]
        public void Should_invoke_module_before_hook_followed_by_resolved_route_followed_by_module_after_hook()
        {
            // Given
            var capturedExecutionOrder = new List<string>();
            var expectedExecutionOrder = new[] { "Prehook", "RouteInvoke", "Posthook" };

            var route = new FakeRoute
            {
                Action = parameters => {
                    capturedExecutionOrder.Add("RouteInvoke");
                    return null;
                }
            };

            var resolvedRoute = new Tuple<Route, DynamicDictionary, Func<NancyContext, Response>, Action<NancyContext>>(
                route, 
                DynamicDictionary.Empty, 
                ctx => { capturedExecutionOrder.Add("Prehook"); return null; }, 
                ctx => capturedExecutionOrder.Add("Posthook"));

            A.CallTo(() => this.routeResolver.Resolve(A<NancyContext>.Ignored)).Returns(resolvedRoute);

            var context = 
                new NancyContext { Request = new Request("GET", "/", "http") };

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            capturedExecutionOrder.Count().ShouldEqual(3);
            capturedExecutionOrder.SequenceEqual(expectedExecutionOrder).ShouldBeTrue();
        }

        [Fact]
        public void Should_not_invoke_resolved_route_if_module_before_hook_returns_response_but_should_invoke_module_after_hook()
        {
            // Given
            var capturedExecutionOrder = new List<string>();
            var expectedExecutionOrder = new[] { "Prehook", "Posthook" };

            var route = new FakeRoute
            {
                Action = parameters =>
                {
                    capturedExecutionOrder.Add("RouteInvoke");
                    return null;
                }
            };

            var resolvedRoute = new Tuple<Route, DynamicDictionary, Func<NancyContext, Response>, Action<NancyContext>>(
                route,
                DynamicDictionary.Empty,
                ctx => {
                    capturedExecutionOrder.Add("Prehook");
                    return new Response();
                },
                ctx => capturedExecutionOrder.Add("Posthook"));

            A.CallTo(() => this.routeResolver.Resolve(A<NancyContext>.Ignored)).Returns(resolvedRoute);

            var context =
                new NancyContext { Request = new Request("GET", "/", "http") };

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            capturedExecutionOrder.Count().ShouldEqual(2);
            capturedExecutionOrder.SequenceEqual(expectedExecutionOrder).ShouldBeTrue();
        }

        [Fact]
        public void Should_return_response_from_module_before_hook_when_not_null()
        {
            // Given
            var moduleBeforeHookResponse = new Response();

            var route = new FakeRoute();

            var resolvedRoute = new Tuple<Route, DynamicDictionary, Func<NancyContext, Response>, Action<NancyContext>>(
                route,
                DynamicDictionary.Empty,
                ctx => moduleBeforeHookResponse,
                ctx => { });

            A.CallTo(() => this.routeResolver.Resolve(A<NancyContext>.Ignored)).Returns(resolvedRoute);

            var context =
                new NancyContext { Request = new Request("GET", "/", "http") };

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            context.Response.ShouldBeSameAs(moduleBeforeHookResponse);
        }

        [Fact]
        public void Should_allow_module_after_hook_to_change_response()
        {
            // Given
            var moduleAfterHookResponse = new Response();

            var route = new FakeRoute();

            var resolvedRoute = new Tuple<Route, DynamicDictionary, Func<NancyContext, Response>, Action<NancyContext>>(
                route,
                DynamicDictionary.Empty,
                ctx => null,
                ctx => ctx.Response = moduleAfterHookResponse);

            A.CallTo(() => this.routeResolver.Resolve(A<NancyContext>.Ignored)).Returns(resolvedRoute);

            var context =
                new NancyContext { Request = new Request("GET", "/", "http") };

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            context.Response.ShouldBeSameAs(moduleAfterHookResponse);
        }

        [Fact]
        public void HandleRequest_should_allow_module_after_hook_to_add_items_to_context()
        {
            // Given
            var route = new FakeRoute();

            var resolvedRoute = new Tuple<Route, DynamicDictionary, Func<NancyContext, Response>, Action<NancyContext>>(
                route,
                DynamicDictionary.Empty,
                ctx => null,
                ctx => ctx.Items.Add("RoutePostReq", new object()));

            A.CallTo(() => this.routeResolver.Resolve(A<NancyContext>.Ignored)).Returns(resolvedRoute);

            var context =
                new NancyContext { Request = new Request("GET", "/", "http") };

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            context.Items.ContainsKey("RoutePostReq").ShouldBeTrue();
        }

        [Fact]
        public void Should_set_the_route_parameters_from_resolved_route()
        {
            // Given
            const string expectedPath = "/the/path";

            var context =
                new NancyContext
                {
                    Request = new FakeRequest("GET", expectedPath)
                };

            var parameters = new DynamicDictionary();

            var resolvedRoute = new ResolveResult(
               new FakeRoute(),
               parameters,
               null,
               null);

            A.CallTo(() => this.routeResolver.Resolve(context)).Returns(resolvedRoute);

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            ((DynamicDictionary)context.Parameters).ShouldBeSameAs(parameters);
        }

        [Fact]
        public void Should_invoke_route_resolver_with_context_for_current_request()
        {
            // Given
            var context = 
                new NancyContext
                {
                    Request = new FakeRequest("GET", "/")
                };

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            A.CallTo(() => this.routeResolver.Resolve(context)).MustHaveHappened(Repeated.Exactly.Once);
        }

        [Fact]
        public void Should_invoke_route_resolver_with_path_when_path_does_not_contain_file_extension()
        {
            // Given
            const string expectedPath = "/the/path";
            var requestedPath = string.Empty;

            var context =
                new NancyContext
                {
                    Request = new FakeRequest("GET", expectedPath)
                };

            var resolvedRoute = new ResolveResult(
               new FakeRoute(),
               DynamicDictionary.Empty,
               null,
               null);

            A.CallTo(() => this.routeResolver.Resolve(context))
                .Invokes(x => requestedPath = ((NancyContext) x.Arguments[0]).Request.Path)
                .Returns(resolvedRoute);

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            requestedPath.ShouldEqual(expectedPath);
        }

        [Fact]
        public void Should_invoke_route_resolver_with_passed_in_accept_headers_when_path_does_not_contain_file_extensions()
        {
            // Given
            var expectedAcceptHeaders = new List<Tuple<string, decimal>>
            {
                { new Tuple<string, decimal>("application/json", 0.8m) },
                { new Tuple<string, decimal>("application/xml", 0.4m) }
            };

            var requestedAcceptHeaders = 
                new List<Tuple<string, decimal>>();

            var request = new FakeRequest("GET", "/")
            {
                Headers = { Accept = expectedAcceptHeaders }
            };

            var context =
                new NancyContext { Request = request };

            var resolvedRoute = new ResolveResult(
               new FakeRoute(),
               DynamicDictionary.Empty,
               null,
               null);

            A.CallTo(() => this.routeResolver.Resolve(context))
                .Invokes(x => requestedAcceptHeaders = ((NancyContext)x.Arguments[0]).Request.Headers.Accept.ToList())
                .Returns(resolvedRoute);

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            requestedAcceptHeaders.ShouldHaveCount(2);
            requestedAcceptHeaders[0].Item1.ShouldEqual("application/json");
            requestedAcceptHeaders[0].Item2.ShouldEqual(0.8m);
            requestedAcceptHeaders[1].Item1.ShouldEqual("application/xml");
            requestedAcceptHeaders[1].Item2.ShouldEqual(0.4m);
        }

        [Fact]
        public void Should_invoke_route_resolver_with_extension_stripped_from_path_when_path_does_contain_file_extension_and_mapped_response_processor_exists()
        {
            // Given
            var requestedPath = string.Empty;

            var context =
                new NancyContext
                {
                    Request = new FakeRequest("GET", "/user.json")
                };

            var processor =
                A.Fake<IResponseProcessor>();

            this.responseProcessors.Add(processor);

            var mappings = new List<Tuple<string, MediaRange>>
            {
                { new Tuple<string, MediaRange>("json", "application/json") }
            };

            A.CallTo(() => processor.ExtensionMappings).Returns(mappings);

            var resolvedRoute = new ResolveResult(
               new FakeRoute(),
               DynamicDictionary.Empty,
               null,
               null);

            A.CallTo(() => this.routeResolver.Resolve(context))
                .Invokes(x => requestedPath = ((NancyContext)x.Arguments[0]).Request.Path)
                .Returns(resolvedRoute);

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            requestedPath.ShouldEqual("/user");
        }

        [Fact]
        public void Should_invoke_route_resolver_with_path_containing_when_path_does_contain_file_extension_and_no_mapped_response_processor_exists()
        {
            // Given
            var requestedPath = string.Empty;

            var context =
                new NancyContext
                {
                    Request = new FakeRequest("GET", "/user.json")
                };

            var resolvedRoute = new ResolveResult(
               new FakeRoute(),
               DynamicDictionary.Empty,
               null,
               null);

            A.CallTo(() => this.routeResolver.Resolve(context))
                .Invokes(x => requestedPath = ((NancyContext)x.Arguments[0]).Request.Path)
                .Returns(resolvedRoute);

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            requestedPath.ShouldEqual("/user.json");
        }

        [Fact]
        public void Should_invoke_route_resolver_with_distinct_mapped_media_ranged_when_path_contains_extension_and_mapped_response_processors_exists()
        {
            // Given
            var requestedAcceptHeaders =
                new List<Tuple<string, decimal>>();

            var context =
                new NancyContext
                {
                    Request = new FakeRequest("GET", "/user.json")
                };

            var jsonProcessor =
                A.Fake<IResponseProcessor>();

            var jsonProcessormappings = 
                new List<Tuple<string, MediaRange>> { { new Tuple<string, MediaRange>("json", "application/json") } };

            A.CallTo(() => jsonProcessor.ExtensionMappings).Returns(jsonProcessormappings);

            var otherProcessor =
                A.Fake<IResponseProcessor>();

            var otherProcessormappings =
                new List<Tuple<string, MediaRange>>
                    {
                        { new Tuple<string, MediaRange>("json", "application/json") },
                        { new Tuple<string, MediaRange>("xml", "application/xml") }
                    };

            A.CallTo(() => otherProcessor.ExtensionMappings).Returns(otherProcessormappings);

            this.responseProcessors.Add(jsonProcessor);
            this.responseProcessors.Add(otherProcessor);

            var resolvedRoute = new ResolveResult(
               new FakeRoute(),
               DynamicDictionary.Empty,
               null,
               null);

            A.CallTo(() => this.routeResolver.Resolve(context))
                .Invokes(x => requestedAcceptHeaders = ((NancyContext)x.Arguments[0]).Request.Headers.Accept.ToList())
                .Returns(resolvedRoute);

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            requestedAcceptHeaders.ShouldHaveCount(1);
            requestedAcceptHeaders[0].Item1.ShouldEqual("application/json");
        }

        [Fact]
        public void Should_set_quality_to_one_point_one_for_mapped_media_ranges_before_invoking_route_resolver_when_path_contains_extension_and_mapped_response_processors_exists()
        {
            // Given
            var requestedAcceptHeaders =
                new List<Tuple<string, decimal>>();

            var context =
                new NancyContext
                {
                    Request = new FakeRequest("GET", "/user.json")
                };

            var jsonProcessor =
                A.Fake<IResponseProcessor>();

            var jsonProcessormappings =
                new List<Tuple<string, MediaRange>> { { new Tuple<string, MediaRange>("json", "application/json") } };

            A.CallTo(() => jsonProcessor.ExtensionMappings).Returns(jsonProcessormappings);

            this.responseProcessors.Add(jsonProcessor);

            var resolvedRoute = new ResolveResult(
               new FakeRoute(),
               DynamicDictionary.Empty,
               null,
               null);

            A.CallTo(() => this.routeResolver.Resolve(context))
                .Invokes(x => requestedAcceptHeaders = ((NancyContext)x.Arguments[0]).Request.Headers.Accept.ToList())
                .Returns(resolvedRoute);

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            requestedAcceptHeaders.ShouldHaveCount(1);
            requestedAcceptHeaders[0].Item2.ShouldEqual(1.1m);
        }

        [Fact]
        public void Should_call_route_invoker_with_resolved_route()
        {
            // Given
            var context =
                new NancyContext
                {
                    Request = new FakeRequest("GET", "/user.json")
                };

            var resolvedRoute = new ResolveResult(
               new FakeRoute(),
               DynamicDictionary.Empty,
               null,
               null);

            A.CallTo(() => this.routeResolver.Resolve(context)).Returns(resolvedRoute);

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            A.CallTo(() => this.routeInvoker.Invoke(resolvedRoute.Item1, A<DynamicDictionary>._, A<NancyContext>._)).MustHaveHappened(Repeated.Exactly.Once);
        }

        [Fact]
        public void Should_invoke_route_resolver_with_path_containing_extension_when_mapped_response_processor_existed_but_no_route_match_was_found()
        {
            // Given
            var requestedAcceptHeaders =
                new List<Tuple<string, decimal>>();

            var context =
                new NancyContext
                {
                    Request = new FakeRequest("GET", "/user.json")
                };

            var jsonProcessor =
                A.Fake<IResponseProcessor>();

            var jsonProcessormappings =
                new List<Tuple<string, MediaRange>> { { new Tuple<string, MediaRange>("json", "application/json") } };

            A.CallTo(() => jsonProcessor.ExtensionMappings).Returns(jsonProcessormappings);

            this.responseProcessors.Add(jsonProcessor);

            var resolvedRoute = new ResolveResult(
               new NotFoundRoute("GET", "/"), 
               DynamicDictionary.Empty,
               null,
               null);

            A.CallTo(() => this.routeResolver.Resolve(context))
                .Invokes(x => requestedAcceptHeaders = ((NancyContext)x.Arguments[0]).Request.Headers.Accept.ToList())
                .Returns(resolvedRoute);

            // When
            this.requestDispatcher.Dispatch(context);

            // Then            
            A.CallTo(() => this.routeResolver.Resolve(A<NancyContext>._)).MustHaveHappened(Repeated.Exactly.Twice);
        }

        [Fact]
        public void Should_call_route_invoker_with_captured_parameters()
        {
            // Given
            var context =
                new NancyContext
                {
                    Request = new FakeRequest("GET", "/user.json")
                };

            var parameters = new DynamicDictionary();

            var resolvedRoute = new ResolveResult(
               new FakeRoute(),
               parameters,
               null,
               null);

            A.CallTo(() => this.routeResolver.Resolve(context)).Returns(resolvedRoute);

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            A.CallTo(() => this.routeInvoker.Invoke(A<Route>._, parameters, A<NancyContext>._)).MustHaveHappened(Repeated.Exactly.Once);
        }

        [Fact]
        public void Should_call_route_invoker_with_context()
        {
            // Given
            var context =
                new NancyContext
                {
                    Request = new FakeRequest("GET", "/user.json")
                };

            var resolvedRoute = new ResolveResult(
               new FakeRoute(),
               DynamicDictionary.Empty,
               null,
               null);

            A.CallTo(() => this.routeResolver.Resolve(context)).Returns(resolvedRoute);

            // When
            this.requestDispatcher.Dispatch(context);

            // Then
            A.CallTo(() => this.routeInvoker.Invoke(A<Route>._, A<DynamicDictionary>._, context)).MustHaveHappened(Repeated.Exactly.Once);
        }
    }
}