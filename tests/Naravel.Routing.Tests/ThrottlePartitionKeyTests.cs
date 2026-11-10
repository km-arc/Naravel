using System.Globalization;

namespace Naravel.Routing.Tests;

/// <summary>
/// The partition key of the <c>throttle</c> middleware (AUDIT D-87, R32.T01): the length written in front of each field lets the
/// key be read back in one way only, so two different (policy, endpoint, subject) triples never share a counter. The text is
/// pinned exactly: in a shared store every instance must compute the same key, so a change of the text splits the counters of
/// instances that run different builds.
/// </summary>
public class ThrottlePartitionKeyTests
{
    [Fact]
    public void The_key_writes_the_length_of_each_field_in_front_of_it()
    {
        ThrottleMiddleware.PartitionKey("5:2", "/limited", "203.0.113.7").Should().Be("3:5:2|8:/limited|11:203.0.113.7");
    }

    [Fact]
    public void A_separator_inside_a_field_cannot_move_to_the_next_field()
    {
        ThrottleMiddleware.PartitionKey("p", "a|b", "c").Should().NotBe(ThrottleMiddleware.PartitionKey("p", "a", "b|c"));
        ThrottleMiddleware.PartitionKey("x|y", "z", "s").Should().NotBe(ThrottleMiddleware.PartitionKey("x", "y|z", "s"));
        ThrottleMiddleware.PartitionKey("p", "e", "1:x").Should().NotBe(ThrottleMiddleware.PartitionKey("p", "e|1", "x"));
    }

    [Fact]
    public void Empty_fields_are_kept_apart_from_each_other()
    {
        ThrottleMiddleware.PartitionKey("", "", "").Should().Be("0:|0:|0:");
        ThrottleMiddleware.PartitionKey("a", "", "b").Should().NotBe(ThrottleMiddleware.PartitionKey("", "a", "b"));
    }

    [Fact]
    public void The_key_can_be_read_back_into_the_triple_it_was_built_from()
    {
        // A key that can be read back has only one triple behind it, which is why two triples cannot share a key. Every field of up
        // to three characters drawn from the separator, the colon, a digit and a letter, empty included: 85 values per field.
        var alphabet = new[] { '|', ':', '1', 'a' };
        var values = new List<string> { string.Empty };
        var shorter = new List<string> { string.Empty };
        for (var length = 1; length <= 3; length++)
        {
            var longer = new List<string>();
            foreach (var prefix in shorter)
            {
                longer.AddRange(alphabet.Select(c => prefix + c));
            }

            values.AddRange(longer);
            shorter = longer;
        }

        var misread = new List<string>();
        foreach (var policy in values)
        {
            foreach (var endpoint in values)
            {
                foreach (var subject in values)
                {
                    var key = ThrottleMiddleware.PartitionKey(policy, endpoint, subject);
                    var read = ReadBack(key);
                    if (read is null || read.Value != (policy, endpoint, subject))
                    {
                        misread.Add($"({Show(policy)}, {Show(endpoint)}, {Show(subject)}) gives {key}");
                    }
                }
            }
        }

        values.Should().HaveCount(85);
        misread.Should().BeEmpty();
    }

    /// <summary>Reads the key from the left: a length, a colon, that many characters, then a <c>|</c> except after the last field.</summary>
    private static (string Policy, string Endpoint, string Subject)? ReadBack(string key)
    {
        var fields = new string[3];
        var at = 0;
        for (var i = 0; i < fields.Length; i++)
        {
            var colon = key.IndexOf(':', at);
            if (colon < 0 || !int.TryParse(key.AsSpan(at, colon - at), NumberStyles.None, CultureInfo.InvariantCulture, out var length) ||
                colon + 1 + length > key.Length)
            {
                return null;
            }

            fields[i] = key.Substring(colon + 1, length);
            at = colon + 1 + length;
            if (i < fields.Length - 1)
            {
                if (at >= key.Length || key[at] != '|') return null;
                at++;
            }
        }

        return at == key.Length ? (fields[0], fields[1], fields[2]) : null;
    }

    private static string Show(string value) => "\"" + value + "\"";
}
