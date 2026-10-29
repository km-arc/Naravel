using System.Text.Json;
using Naravel.Broadcasting;

namespace Naravel.Broadcasting.Tests;

/// <summary>The reflection-free <see cref="PresenceMember"/> constructor (trimming and Native AOT): information that is already JSON.</summary>
public class PresenceMemberJsonTests
{
    [Fact]
    public void A_JsonElement_is_kept_as_is_and_survives_its_document()
    {
        PresenceMember member;
        using (var document = JsonDocument.Parse("""{"name":"Ann","tags":[1,2]}"""))
        {
            member = new PresenceMember("1", document.RootElement);
        }

        member.UserId.Should().Be("1");
        member.Info!.Value.GetRawText().Should().Be("""{"name":"Ann","tags":[1,2]}""");
    }

    [Fact]
    public void A_JsonElement_larger_than_the_limit_is_rejected_like_an_object()
    {
        using var document = JsonDocument.Parse("\"" + new string('x', 2100) + "\"");

        var element = () => new PresenceMember("1", document.RootElement);
        var obj = () => new PresenceMember("1", new string('x', 2100));

        element.Should().Throw<ArgumentException>().WithParameterName("info");
        obj.Should().Throw<ArgumentException>().WithParameterName("info");
    }

    [Fact]
    public void A_blank_user_id_is_rejected()
    {
        using var document = JsonDocument.Parse("{}");

        var act = () => new PresenceMember(" ", document.RootElement);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_JsonElement_and_object_constructors_produce_the_same_info()
    {
        using var document = JsonDocument.Parse("""{"name":"Ann"}""");

        var fromElement = new PresenceMember("1", document.RootElement);
        var fromObject = new PresenceMember("1", new { name = "Ann" });

        fromElement.Info!.Value.GetRawText().Should().Be(fromObject.Info!.Value.GetRawText());
    }
}
