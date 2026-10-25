using System.Security.Claims;

namespace Naravel.Broadcasting.Tests;

public class ChannelRulesTests
{
    private static readonly ClaimsPrincipal User = new(new ClaimsIdentity([new Claim("order", "7")], "test"));

    private static ChannelRules Rules() => new BroadcastingOptions().Channels;

    [Fact]
    public async Task A_placeholder_is_passed_to_the_rule_and_the_rule_decides()
    {
        var rules = Rules().Authorize("private-orders.{id}", (user, id) => user.FindFirst("order")?.Value == id);

        (await rules.AuthorizeAsync("private-orders.7", User, default)).Allowed.Should().BeTrue();
        (await rules.AuthorizeAsync("private-orders.8", User, default)).Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task A_channel_with_no_matching_rule_is_denied()
    {
        var rules = Rules().Authorize("private-orders.{id}", (_, _) => true);

        (await rules.AuthorizeAsync("private-invoices.7", User, default)).Allowed.Should().BeFalse();
        (await Rules().AuthorizeAsync("private-orders.7", User, default)).Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task The_pattern_must_match_the_whole_name_and_a_placeholder_stops_at_a_dot()
    {
        var rules = Rules().Authorize("private-orders.{id}", (_, _) => true);

        (await rules.AuthorizeAsync("private-orders.7.items", User, default)).Allowed.Should().BeFalse();
        (await rules.AuthorizeAsync("xprivate-orders.7", User, default)).Allowed.Should().BeFalse();
        (await rules.AuthorizeAsync("private-orders.", User, default)).Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task Several_placeholders_arrive_by_name_and_the_first_matching_rule_decides()
    {
        IReadOnlyDictionary<string, string>? seen = null;
        var rules = Rules()
            .Authorize("private-teams.{team}.users.{user}", (_, values, _) =>
            {
                seen = values;
                return ValueTask.FromResult(values["user"] == "1");
            })
            .Authorize("private-teams.{team}.users.{user}", (_, _, _) => ValueTask.FromResult(true));

        (await rules.AuthorizeAsync("private-teams.a.users.2", User, default)).Allowed.Should().BeFalse();
        seen.Should().NotBeNull();
        seen!["team"].Should().Be("a");
        seen["user"].Should().Be("2");
    }

    [Fact]
    public async Task A_presence_rule_returns_the_member_and_null_refuses()
    {
        var rules = Rules().AuthorizePresence("presence-room.{id}", (user, id) =>
            id == "1" ? new PresenceMember(user.FindFirst("order")!.Value, new { name = "Ann" }) : null);

        var allowed = await rules.AuthorizeAsync("presence-room.1", User, default);
        var refused = await rules.AuthorizeAsync("presence-room.2", User, default);

        allowed.Allowed.Should().BeTrue();
        allowed.Member!.UserId.Should().Be("7");
        allowed.Member.Info!.Value.GetProperty("name").GetString().Should().Be("Ann");
        refused.Allowed.Should().BeFalse();
    }

    [Fact]
    public void A_private_rule_cannot_use_a_presence_pattern_and_the_other_way_round()
    {
        var a = () => Rules().Authorize("presence-room.{id}", (_, _) => true);
        var b = () => Rules().AuthorizePresence("private-room.{id}", (_, _) => null);
        a.Should().Throw<ArgumentException>();
        b.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Member_information_is_limited_in_size()
    {
        var act = () => new PresenceMember("1", new { text = new string('x', 3000) });
        act.Should().Throw<ArgumentException>();
        var blank = () => new PresenceMember(" ");
        blank.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("orders.{id}")]
    [InlineData("")]
    [InlineData("private-orders.{id")]
    [InlineData("private-orders.id}")]
    [InlineData("private-orders.{1id}")]
    [InlineData("private-orders.{id}.{id}")]
    public void A_bad_pattern_is_refused_when_it_is_registered(string pattern)
    {
        var act = () => Rules().Authorize(pattern, (_, _, _) => ValueTask.FromResult(true));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_single_placeholder_overload_refuses_a_pattern_with_another_number_of_placeholders()
    {
        var none = () => Rules().Authorize("private-lobby", (_, _) => true);
        var two = () => Rules().Authorize("private-a.{x}.{y}", (_, _) => true);
        none.Should().Throw<ArgumentException>();
        two.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("orders.7", true)]
    [InlineData("a_b:c-d", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("slash/name", false)]
    [InlineData("é", false)]
    public void Channel_names_use_a_small_alphabet(string name, bool valid) => ChannelNames.IsValid(name).Should().Be(valid);

    [Fact]
    public void Channel_names_have_a_length_limit() =>
        ChannelNames.IsValid(new string('a', ChannelNames.MaxLength + 1)).Should().BeFalse();
}
