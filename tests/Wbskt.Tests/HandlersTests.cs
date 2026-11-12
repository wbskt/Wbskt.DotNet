using FluentAssertions;
using Wbskt.Internal;

namespace Wbskt.Tests;

public sealed class HandlersTests
{
    [Fact]
    public void A_handler_that_throws_reaches_the_error_handler_and_the_next_subscriber_still_runs()
    {
        Exception? seen = null;
        var secondRan = false;
        Action? handlers = () => throw new InvalidOperationException("boom");
        handlers += () => secondRan = true;

        Handlers.Raise(handlers, error => seen = error);

        seen.Should().BeOfType<InvalidOperationException>();
        secondRan.Should().BeTrue();
    }

    [Fact]
    public void An_error_handler_that_throws_is_ignored()
    {
        var raise = () => Handlers.Raise(() => throw new InvalidOperationException("boom"), _ => throw new InvalidOperationException("again"));

        raise.Should().NotThrow();
    }

    [Fact]
    public void Every_error_handler_hears_of_it_even_when_one_throws()
    {
        var heard = 0;
        Action<Exception>? onError = _ => throw new InvalidOperationException("again");
        onError += _ => heard++;

        Handlers.Raise(() => throw new InvalidOperationException("boom"), onError);

        heard.Should().Be(1);
    }
}
