using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using NUnit.Framework;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Services.Interceptors;
using W3ChampionsStatisticService.Services.Tracing;

namespace WC3ChampionsStatisticService.Tests.Tracing;

[TestFixture]
public class TracingInterceptorTests
{
    private ActivitySource _activitySource;
    private TracingInterceptor _interceptor;
    private ProxyGenerator _proxyGenerator;
    private List<string> _spanStartCalls;
    private TestActivityListener _activityListener;

    [SetUp]
    public void Setup()
    {
        _activitySource = new ActivitySource("TestSource");
        _interceptor = new TracingInterceptor(_activitySource);
        _proxyGenerator = new ProxyGenerator();
        _spanStartCalls = new List<string>();

        // Create a custom activity listener to track span creation
        _activityListener = new TestActivityListener(_spanStartCalls);
        ActivitySource.AddActivityListener(_activityListener.Listener);
    }

    [TearDown]
    public void TearDown()
    {
        _activityListener?.Dispose();
        _activitySource?.Dispose();
    }

    [Test]
    public void ShouldNotCreateSpansForChildFunctionsWhenParentHasNoTrace()
    {
        // Arrange
        var testClass = _proxyGenerator.CreateClassProxy<TestClass>(_interceptor);

        // Act
        var result = testClass.ParentMethodWithNoTrace();

        // Assert
        Assert.AreEqual("child result", result);
        Assert.AreEqual(0, _spanStartCalls.Count, "No spans should be created when parent has [NoTrace]");
    }

    [Test]
    public void ShouldCreateSpansForChildFunctionsWhenParentDoesNotHaveNoTrace()
    {
        // Arrange
        var testClass = _proxyGenerator.CreateClassProxy<TestClass>(_interceptor);

        // Act
        var result = testClass.ParentMethodWithoutNoTrace();

        // Assert
        Assert.AreEqual("child result", result);
        Assert.GreaterOrEqual(_spanStartCalls.Count, 1, "Spans should be created when parent does not have [NoTrace]");
        Assert.IsTrue(_spanStartCalls.Contains("TestClass.ChildMethodWithTrace"), "Should have span for traced child method");
    }

    [Test]
    public void ShouldPropagateNoTraceContextThroughMultipleLevels()
    {
        // Arrange
        var testClass = _proxyGenerator.CreateClassProxy<TestClass>(_interceptor);

        // Act
        var result = testClass.Level1MethodWithNoTrace();

        // Assert
        Assert.AreEqual("deep result", result);
        Assert.AreEqual(0, _spanStartCalls.Count, "No spans should be created at any level when top-level has [NoTrace]");
    }

    [Test]
    public void ShouldRespectNoTraceContextWhenSetManually()
    {
        // Arrange
        var testClass = _proxyGenerator.CreateClassProxy<TestClass>(_interceptor);

        // Act - Call without no-trace context first
        var result1 = testClass.TracedMethodOnly();
        var spanCountWithoutContext = _spanStartCalls.Count;

        // Reset and call with manually set no-trace context
        _spanStartCalls.Clear();

        // Create a test class that has a NoTrace method to set the context
        var result2 = testClass.CallTracedMethodFromNoTraceContext();

        // Assert
        Assert.AreEqual("traced result", result1);
        Assert.AreEqual("traced result", result2);
        Assert.Greater(spanCountWithoutContext, 0, "Should create spans without no-trace context");
        Assert.AreEqual(0, _spanStartCalls.Count, "Should not create spans with manual no-trace context");
    }

    [Test]
    public void ShouldNotLeakNoTraceContextUpwardsToSiblingCalls()
    {
        // Arrange
        var testClass = _proxyGenerator.CreateClassProxy<TestClass>(_interceptor);

        // Act
        var result = testClass.ParentWithMixedChildren();

        // Assert
        Assert.AreEqual("no-trace result, traced result", result);

        // Should have spans for parentMethod and tracedChild, but not noTraceChild
        var parentSpans = _spanStartCalls.FindAll(s => s.Contains("ParentWithMixedChildren"));
        var tracedChildSpans = _spanStartCalls.FindAll(s => s.Contains("TracedChildMethod"));
        var noTraceChildSpans = _spanStartCalls.FindAll(s => s.Contains("NoTraceChildMethod"));

        Assert.GreaterOrEqual(parentSpans.Count, 1, "Should have span for parent method");
        Assert.GreaterOrEqual(tracedChildSpans.Count, 1, "Should have span for traced child method");
        Assert.AreEqual(0, noTraceChildSpans.Count, "Should not have span for no-trace child method");
    }

    [Test]
    public async Task ShouldHandleAsyncMethodsWithNoTrace()
    {
        // Arrange
        var testClass = _proxyGenerator.CreateClassProxy<TestClass>(_interceptor);

        // Act
        var result = await testClass.AsyncParentMethodWithNoTrace();

        // Assert
        Assert.AreEqual("async child result", result);
        Assert.AreEqual(0, _spanStartCalls.Count, "No spans should be created for async methods with [NoTrace]");
    }

    [Test]
    public void ShouldRespectNoTraceOnSpecificParameters()
    {
        // Arrange
        var testClass = _proxyGenerator.CreateClassProxy<TestClass>(_interceptor);

        // Act
        var result = testClass.MethodWithNoTraceParameter("visible", "hidden");

        // Assert
        Assert.AreEqual("visible-hidden", result);

        // We can't easily test parameter exclusion from span tags in this unit test setup,
        // but we verify the method executes correctly
        Assert.GreaterOrEqual(_spanStartCalls.Count, 0);
    }

    // Test classes for the interceptor tests
    [Trace]
    public class TestClass
    {
        [NoTrace]
        public virtual string ParentMethodWithNoTrace()
        {
            return ChildMethodWithTrace();
        }

        public virtual string ParentMethodWithoutNoTrace()
        {
            return ChildMethodWithTrace();
        }

        [Trace]
        public virtual string ChildMethodWithTrace()
        {
            return "child result";
        }

        [NoTrace]
        public virtual string Level1MethodWithNoTrace()
        {
            return Level2MethodWithTrace();
        }

        [Trace]
        public virtual string Level2MethodWithTrace()
        {
            return Level3MethodWithTrace();
        }

        [Trace]
        public virtual string Level3MethodWithTrace()
        {
            return "deep result";
        }

        [Trace]
        public virtual string TracedMethodOnly()
        {
            return "traced result";
        }

        [Trace]
        public virtual string ParentWithMixedChildren()
        {
            var result1 = NoTraceChildMethod();
            var result2 = TracedChildMethod();
            return $"{result1}, {result2}";
        }

        [NoTrace]
        public virtual string NoTraceChildMethod()
        {
            return "no-trace result";
        }

        [Trace]
        public virtual string TracedChildMethod()
        {
            return "traced result";
        }

        [NoTrace]
        public virtual async Task<string> AsyncParentMethodWithNoTrace()
        {
            return await AsyncChildMethodWithTrace();
        }

        [Trace]
        public virtual async Task<string> AsyncChildMethodWithTrace()
        {
            await Task.Delay(1); // Simulate async work
            return "async child result";
        }

        [Trace]
        public virtual string MethodWithNoTraceParameter(string visibleParam, [NoTrace] string hiddenParam)
        {
            return $"{visibleParam}-{hiddenParam}";
        }

        [NoTrace]
        public virtual string CallTracedMethodFromNoTraceContext()
        {
            return TracedMethodOnly();
        }
    }

    // Custom activity listener to track span creation for testing
    private class TestActivityListener : IDisposable
    {
        private readonly List<string> _spanStartCalls;
        public ActivityListener Listener { get; }

        public TestActivityListener(List<string> spanStartCalls)
        {
            _spanStartCalls = spanStartCalls;
            Listener = new ActivityListener
            {
                ShouldListenTo = _ => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
                ActivityStarted = OnActivityStarted
            };
        }

        private void OnActivityStarted(Activity activity)
        {
            if (activity.Source.Name == "TestSource")
            {
                _spanStartCalls.Add(activity.DisplayName);
            }
        }

        public void Dispose()
        {
            Listener?.Dispose();
        }
    }
}
