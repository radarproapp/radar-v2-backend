using RadarV2.Services;

namespace RadarV2.Tests;

public class DomainVocabularyTests
{
    [Theory]
    [InlineData("social work")]
    [InlineData("Social Work and Community Development")]
    [InlineData("sociology")]
    [InlineData("soccer coaching")]
    [InlineData("society and culture")]
    [InlineData("associate degree")]
    public void A_word_containing_soc_is_not_cybersecurity(string subject)
    {
        // The live bug this replaces: every copy of the keyword list used Contains("soc"), which
        // matches "social work" and "sociology". A social-work learner was routed to the
        // cybersecurity roadmap, the cybersecurity Learn Hub and the cybersecurity resources.
        Assert.False(DomainVocabulary.Matches(subject, "cyber"));
        Assert.NotEqual("cyber", DomainVocabulary.FieldOf(subject));
    }

    [Theory]
    [InlineData("SOC analyst")]
    [InlineData("soc analyst role")]
    [InlineData("I want to work in a SOC")]
    public void But_soc_standing_alone_still_is(string subject)
    {
        Assert.True(DomainVocabulary.Matches(subject, "cyber"));
    }

    [Theory]
    [InlineData("Cybersecurity")]
    [InlineData("cyber security")]
    [InlineData("infosec")]
    [InlineData("information security")]
    [InlineData("digital forensics")]
    [InlineData("malware analysis")]
    [InlineData("penetration testing")]
    [InlineData("ethical hacking")]
    [InlineData("threat intelligence")]
    [InlineData("SIEM engineering")]
    public void Every_way_of_saying_cybersecurity_lands_in_one_field(string subject)
    {
        // These used to disagree between services: "infosec" was only in the Learn Hub's copy and
        // "forensic" was missing from it, so one profile could get a cybersecurity roadmap and a
        // data-analytics Learn Hub at the same time.
        Assert.True(DomainVocabulary.Matches(subject, "cyber"), subject);
        Assert.Equal("cyber", DomainVocabulary.FieldOf(subject));
    }

    [Theory]
    [InlineData("Nursing")]
    [InlineData("Commercial law")]
    [InlineData("Agricultural economics")]
    [InlineData("Civil engineering is my field")]
    [InlineData("Fashion design")]
    [InlineData("Teaching")]
    public void An_unlisted_field_is_never_quietly_made_cybersecurity(string subject)
    {
        Assert.False(DomainVocabulary.Matches(subject, "cyber"));
    }

    [Fact]
    public void Excel_does_not_match_excellent()
    {
        // "excel" is a prefix of "excellent", so a prefix rule would make every advert that says
        // "excellent communication skills" a data-analytics match.
        Assert.False(DomainVocabulary.Matches("excellent communication skills", "data"));
        Assert.True(DomainVocabulary.Matches("Excel and SQL reporting", "data"));
    }

    [Fact]
    public void A_subject_is_found_in_a_longer_piece_of_text()
    {
        var advert = "Graduate Nurse Residency Programme at Lagos University Teaching Hospital";

        Assert.True(DomainVocabulary.MentionsSubject(advert, "Nursing"));
        Assert.False(DomainVocabulary.MentionsSubject(advert, "Cybersecurity"));
    }

    [Fact]
    public void A_cyber_subject_matches_an_advert_that_never_says_cyber()
    {
        var advert = "Join our SOC as a Tier 1 analyst working with SIEM tooling";

        Assert.True(DomainVocabulary.MentionsSubject(advert, "Cybersecurity"));
    }

    [Fact]
    public void Very_short_words_are_ignored_when_matching_a_subject()
    {
        // "IT" and "AI" appear inside ordinary words; a false domain match is worth 26 points in
        // opportunity scoring, far more than a missed one costs.
        Assert.Empty(DomainVocabulary.OwnWords("IT"));
        Assert.Empty(DomainVocabulary.OwnWords("AI"));
    }

    [Fact]
    public void Nothing_in_means_nothing_out()
    {
        // A user with no stated goal scores no domain points, rather than being assigned a field.
        Assert.Null(DomainVocabulary.FieldOf(null));
        Assert.Null(DomainVocabulary.FieldOf(""));
        Assert.False(DomainVocabulary.MentionsSubject("anything at all", null));
        Assert.False(DomainVocabulary.MentionsSubject(null, "Nursing"));
    }

    [Theory]
    [InlineData("Nursing", "Graduate Nurse Residency Programme")]
    [InlineData("Teaching", "Secondary school teacher wanted")]
    [InlineData("Economics", "Economic policy research fellowship")]
    [InlineData("Engineering", "Graduate engineer programme")]
    [InlineData("Midwifery", "Midwife training bursary")]
    [InlineData("Surgery", "Trainee surgeon post")]
    public void A_subject_matches_the_word_form_the_advert_uses(string subject, string advert)
    {
        // Unlisted fields have no synonym list to fall back on, so if "Nursing" does not reach
        // "Nurse" they match nothing at all - the exact users this is supposed to serve.
        Assert.True(DomainVocabulary.MentionsSubject(advert, subject), subject + " vs " + advert);
    }

    [Fact]
    public void Trimming_endings_does_not_collapse_unrelated_words()
    {
        // The reason "y" is not trimmed: "policy" would become "polic" and match "police".
        Assert.False(DomainVocabulary.MentionsSubject("Police recruitment drive", "Policy"));
    }

    [Fact]
    public void Matching_is_not_case_sensitive()
    {
        Assert.True(DomainVocabulary.Matches("CYBERSECURITY", "cyber"));
        Assert.True(DomainVocabulary.Matches("cybersecurity", "cyber"));
    }
}
