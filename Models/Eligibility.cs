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
