using MongoDB.Bson.Serialization.Attributes;

namespace RadarV2.Models;

/// <summary>
/// What somebody must be, or have, to be able to apply.
/// </summary>
/// <remarks>
/// The distinction the whole engine turns on: relevance asks whether something is about what you
/// care about, eligibility asks whether you could actually get it. An AI-interested student and
/// an AI-interested director both match "AI"; only one of them should be shown the senior role.
///
/// Until now this existed only as <see cref="Opportunity.WhoIsEligible"/>, a list of sentences.
/// Prose can be displayed and cannot be matched, so nothing could compare a posting against a
/// person - and showing somebody a job they cannot get is worse than showing them nothing,
/// because it costs them an application.
///
/// Every field is optional and every absent field means "not stated", never "no restriction".
/// A source that does not say who may apply has told us nothing, and the difference between
/// those two readings is the difference between a careful recommendation and a guess.
/// </remarks>
public class Eligibility
{
    /// <summary>
    /// Where these criteria came from, which decides how far they are trusted.
    /// </summary>
    /// <remarks>
    /// Added after a review executed the extractor against real Nigerian job-board text and found
    /// it fired wrongly more often than rightly. "Not more than 14 days from the date of this
    /// publication" became an age limit of 14. "With over 15 years of experience in the Nigerian
    /// market" - company boilerplate - became an experience floor and an age floor. "A university
    /// degree is not required" became a requirement for one. Each of those hid the posting from
    /// people who qualified, silently.
    ///
    /// Better regexes are part of the answer and cannot be all of it, because the failure is not
    /// that the patterns were careless - it is that a machine reading somebody else's prose will
    /// sometimes be wrong, and the cost of being wrong here falls entirely on the person who
    /// never sees what they lost.
    ///
    /// So provenance is recorded and the check reads it: criteria Radar extracted can inform and
    /// cannot exclude. The asymmetry the design always claimed - a missed criterion is cheap, a
    /// wrongly read one is not - is now enforced by structure rather than by pattern quality.
    /// </remarks>
    public EligibilitySource Source { get; set; } = EligibilitySource.Stated;

    /// <summary>Nationalities that may apply, empty when the source did not say.</summary>
    /// <remarks>
    /// Kept apart from <see cref="Countries"/> because they are different claims. A scholarship
    /// can be open to Nigerian nationals living anywhere, or to anybody resident in Nigeria, and
    /// a person can satisfy one without the other.
    /// </remarks>
    public List<string> Nationalities { get; set; } = [];

    /// <summary>Countries of residence or study that may apply.</summary>
    public List<string> Countries { get; set; } = [];

    /// <summary>
    /// The lowest qualification that qualifies, if one is stated.
    /// </summary>
    /// <remarks>
    /// A floor rather than a list, because that is how postings are written - "a bachelor's
    /// degree or higher". Somebody above the floor is eligible; the ranking can still decide a
    /// PhD is a poor fit for a graduate internship.
    /// </remarks>
    [BsonIgnoreIfNull]
    public QualificationLevel? MinimumQualification { get; set; }

    /// <summary>Years of professional experience expected, when stated.</summary>
    [BsonIgnoreIfNull]
    public int? MinimumYearsExperience { get; set; }

    [BsonIgnoreIfNull]
    public int? MaximumYearsExperience { get; set; }

    /// <summary>The least senior somebody may be, where a posting says.</summary>
    /// <remarks>
    /// Seniority and years of experience are not the same claim and a posting may state either.
    /// Six years in one role and six across four are different seniorities, and "we want a
    /// manager" is a statement about scope rather than duration.
    ///
    /// This is the field <see cref="UserProfile.SeniorityLevel"/> is compared against. Without it
    /// the profile held a seniority with nothing on the other side of the comparison, which is
    /// half a feature and reads as a whole one.
    /// </remarks>
    [BsonIgnoreIfNull]
    public Seniority? MinimumSeniority { get; set; }

    /// <summary>
    /// The most senior somebody may be, where a programme is aimed at a level.
    /// </summary>
    /// <remarks>
    /// Not redundant with the floor. A graduate scheme excluding a director is a real rule, and
    /// it is the direction that catches the mistake Radar is most likely to make: showing
    /// somebody an opportunity they are far too senior for because it matched on subject.
    /// </remarks>
    [BsonIgnoreIfNull]
    public Seniority? MaximumSeniority { get; set; }

    /// <summary>Skills the posting asks for.</summary>
    /// <remarks>
    /// Matched against what somebody says they can do and, once Radar Learn can vouch for it,
    /// against what they have been assessed on. A claimed skill and a verified one are not the
    /// same evidence, so the match says which it used.
    /// </remarks>
    public List<string> RequiredSkills { get; set; } = [];

    /// <summary>
    /// Certifications a posting asks for, which are not a rung on the academic ladder.
    /// </summary>
    /// <remarks>
    /// Their own field because they were briefly a <see cref="QualificationLevel"/> value, and a
    /// value above Doctorate at that - so a PhD failed a "PMP required" floor, and a CompTIA A+
    /// cleared a "bachelor's degree or higher" one. Both wrong, and wrong in opposite directions,
    /// because a certification is orthogonal to how far somebody got in formal education rather
    /// than further along it.
    /// </remarks>
    public List<string> RequiredCertifications { get; set; } = [];

    /// <summary>An age range, where a programme states one.</summary>
    /// <remarks>
    /// Common in youth fellowships and graduate schemes, and the one criterion nobody can
    /// acquire. Worth holding so Radar never recommends something a person has aged out of.
    /// </remarks>
    [BsonIgnoreIfNull]
    public int? MinimumAge { get; set; }

    [BsonIgnoreIfNull]
    public int? MaximumAge { get; set; }

    /// <summary>Whether anything at all was recorded.</summary>
    /// <remarks>
    /// An opportunity with nothing stated is not open to everybody - it is one nobody has read
    /// the criteria for. The difference matters on a screen that claims to check eligibility,
    /// and a caller that cannot tell them apart will assert something nothing established.
    /// </remarks>
    [BsonIgnore]
    public bool WasStated =>
        Nationalities?.Count > 0
        || Countries?.Count > 0
        || MinimumQualification is not null
        || MinimumYearsExperience is not null

        // Every field, and this one was missed. A graduate programme whose only stated criterion
        // is "no more than two years' experience" read as having stated nothing - so the posting
        // whose single rule excludes a six-year professional was the one Radar called unchecked.
        || MaximumYearsExperience is not null
        || MinimumSeniority is not null
        || MaximumSeniority is not null

        || RequiredSkills?.Count > 0
        || RequiredCertifications?.Count > 0
        || MinimumAge is not null
        || MaximumAge is not null;
}

/// <summary>
/// Who said so.
/// </summary>
public enum EligibilitySource
{
    /// <summary>
    /// The source stated it, or a person recorded it.
    /// </summary>
    /// <remarks>
    /// Trusted to exclude. The default, because anything written by hand into this model is
    /// somebody asserting it on purpose.
    /// </remarks>
    Stated,

    /// <summary>
    /// Radar read it out of the posting's prose.
    /// </summary>
    /// <remarks>
    /// Trusted to inform and never to exclude. A reader still sees "this asks for a bachelor's
    /// degree and you have an OND", which is useful; what they do not get is the posting
    /// disappearing on the strength of a regex that misread a sentence about hours per week.
    /// </remarks>
    Extracted,
}

/// <summary>
/// How far somebody has got in formal education.
/// </summary>
/// <remarks>
/// Ordered deliberately, so "a bachelor's degree or higher" is a comparison rather than a set.
/// Appended to rather than reordered if more are added: these persist by value, so inserting one
/// in the middle would silently relabel every record already stored.
/// </remarks>
public enum QualificationLevel
{
    /// <summary>Still studying, with nothing awarded yet.</summary>
    InProgress,

    SecondarySchool,
    Nce,
    Ond,
    Hnd,
    Bachelors,
    Masters,
    Doctorate,

    // Nothing below Doctorate, deliberately and permanently. A professional certification was
    // briefly a member here and sat above a PhD, which made the floor comparison wrong in both
    // directions; certifications now live in Eligibility.RequiredCertifications, where they are
    // not being ranked against degrees at all.
    //
    // Anything added must be appended, never inserted: these persist by value, so a new member
    // in the middle silently relabels every record already stored. EnumValueTests pins all of
    // them, because four comments saying so enforced nothing.
}
