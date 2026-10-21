using RadarV2.Services;

namespace RadarV2.Tests;

public class DomainVocabularyTests
{
    [Fact]
    public void A_listed_field_gains_the_words_adverts_actually_use()
    {
        // Someone who types "Cybersecurity" should still match a posting that only ever says
        // "SOC analyst" — the whole reason synonyms exist.
        var words = DomainVocabulary.Expand("Cybersecurity");

        Assert.Contains("soc", words);
        Assert.Contains("threat", words);
    }

    [Fact]
    public void An_unlisted_field_does_not_borrow_another_fields_words()
    {
        // The bug this guards: cybersecurity was the example everyone built from, so it was the
        // only field with synonyms, and a nursing learner was scored against "security".
        var words = DomainVocabulary.Expand("Nursing");

        Assert.Contains("nursing", words);
        Assert.DoesNotContain("security", words);
        Assert.DoesNotContain("soc", words);
        Assert.DoesNotContain("data", words);
    }

    [Theory]
    [InlineData("Nursing")]
    [InlineData("Commercial law")]
    [InlineData("Agricultural economics")]
    [InlineData("Civil engineering")]
    public void Every_field_matches_on_its_own_words(string subject)
    {
        // An unlisted field has to work as well as a listed one, not fall off a cliff.
        var words = DomainVocabulary.Expand(subject);

        Assert.NotEmpty(words);
        Assert.All(words, w => Assert.False(string.IsNullOrWhiteSpace(w)));
    }

    [Fact]
    public void Very_short_words_are_dropped()
    {
        // "IT" and "AI" are real subjects, but as substrings they appear in almost any text — and
        // a false domain match is worth 26 points here, far more than a missed one costs.
        Assert.DoesNotContain("it", DomainVocabulary.Expand("IT support"));
        Assert.DoesNotContain("ai", DomainVocabulary.Expand("AI research"));
    }

    [Fact]
    public void Nothing_in_means_nothing_out()
    {
        // A user with no stated goal should score no domain points, rather than being quietly
        // assigned a default field.
        Assert.Empty(DomainVocabulary.Expand(null));
        Assert.Empty(DomainVocabulary.Expand(""));
        Assert.Empty(DomainVocabulary.Expand("   "));
    }

    [Fact]
    public void Matching_is_not_case_sensitive()
    {
        Assert.Contains("soc", DomainVocabulary.Expand("CYBERSECURITY"));
        Assert.Contains("soc", DomainVocabulary.Expand("cybersecurity"));
    }

    [Fact]
    public void A_subject_naming_two_fields_gets_both()
    {
        var words = DomainVocabulary.Expand("Security data analytics");

        Assert.Contains("soc", words);
        Assert.Contains("sql", words);
    }

    [Fact]
    public void The_same_word_is_not_returned_twice()
    {
        // "security" is both the user's own word and a synonym of the cyber field.
        var words = DomainVocabulary.Expand("security");

        Assert.Equal(words.Length, words.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
