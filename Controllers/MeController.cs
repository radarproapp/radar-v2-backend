using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services.Interfaces;
using RadarV2.Services;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/me")]
public class MeController : ControllerBase
{
    private readonly IUserProfileService _profiles;
    private readonly IBehavioralSignalService _signals;
    private readonly UserGraphService _graph;

    public MeController(
        IUserProfileService profiles,
        IBehavioralSignalService signals,
        UserGraphService graph)
    {
        _profiles = profiles;
        _signals = signals;
        _graph = graph;
    }

    /// <summary>
    /// Everything Radar can honestly say this user has done.
    /// </summary>
    /// <remarks>
    /// Derived from analytics events, playback rows, roadmaps, quizzes, applications, notes and
    /// projects on every request. UserProfile.Stats declared most of these fields already and
    /// nothing ever wrote to them, so the Growth Tracker showed nine tiles of zero to everybody —
    /// which is why this computes rather than reads a counter.
    /// </remarks>
    [HttpGet("graph")]
    public async Task<IActionResult> GetGraphAsync(CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _graph.BuildAsync(profile.Id, ct));
    }

    /// <summary>
    /// Editable profile fields. Email/id/stats are managed server-side only —
    /// editing email would desync the auth 'users' collection, so it is excluded.
    /// </summary>
    public sealed class UpdateProfileRequest
    {
        public string? Name { get; set; }
        public string? Persona { get; set; }
        public string? PrimaryGoal { get; set; }
        public string? Region { get; set; }
        public string? City { get; set; }
        public List<string>? Interests { get; set; }
        public List<UserInterestContext>? InterestContexts { get; set; }
        public List<string>? DominantInterests { get; set; }
        public List<string>? Problems { get; set; }
        public string? CurrentIntent { get; set; }
        public string? TargetRole { get; set; }
        public string? TargetIndustry { get; set; }
        public List<string>? Capabilities { get; set; }

        // The facts an eligibility check needs and onboarding deliberately does not ask for.
        // The brief keeps onboarding to three questions and lets Radar learn the rest, so these
        // belong on a profile screen - asked once an opportunity turns out to need them, rather
        // than up front of everybody.
        public List<string>? Nationalities { get; set; }
        public string? CountryOfResidence { get; set; }
        public int? YearsOfExperience { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public List<string>? OpportunityPreferences { get; set; }
        public List<string>? Geography { get; set; }
        public List<string>? DecisionNeeds { get; set; }
        public Dictionary<string, string>? PersonaDetails { get; set; }
        public NotificationPrefs? Notifications { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        return profile is null
            ? NotFound(new { error = "Profile not found" })
            : Ok(profile);
    }

    [HttpGet("focus")]
    public async Task<IActionResult> GetFocusAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var paths = InterestPathService.Build(profile);
        var active = InterestPathService.ActiveInterests(profile).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suggestions = profile.InterestActivityScores
            .Where(pair => !active.Contains(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .Take(2)
            .Select(pair => pair.Key)
            .ToList();

        return Ok(new { paths, suggestedDominantInterests = suggestions });
    }

    /// <summary>
    /// Progressive follow-up questions Radar can ask when there is a reason — an inferred interest
    /// it hasn't confirmed, a shift in focus, or a profile gap that would sharpen personalization.
    /// </summary>
    [HttpGet("follow-ups")]
    public async Task<IActionResult> GetFollowUpsAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var top = await _signals.GetTopSignalsAsync(profile.Id, 12);
        var all = await _signals.GetSignalsAsync(profile.Id);
        return Ok(FollowUpService.Suggest(profile, top, all));
    }

    /// <summary>
    /// What Radar has learned about this user: top signals by recency-decayed strength, plus the
    /// overall confidence in the model. Declared and inferred signals are reported separately so the
    /// client can distinguish what the user told us from what we inferred from their behaviour.
    /// </summary>
    [HttpGet("signals")]
    public async Task<IActionResult> GetSignalsAsync([FromQuery] int take = 12)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var all = await _signals.GetSignalsAsync(profile.Id);
        var top = await _signals.GetTopSignalsAsync(profile.Id, Math.Clamp(take, 1, 50));

        return Ok(new
        {
            profileConfidence = await _signals.GetProfileConfidenceAsync(profile.Id),
            declaredCount = all.Count(s => s.Source == SignalSource.Declared),
            inferredCount = all.Count(s => s.Source == SignalSource.Inferred),
            signals = top.Select(s => new
            {
                s.Term,
                s.Source,
                s.Strength,
                s.Confidence,
                s.ExplicitFeedback,
                s.PositiveCount,
                s.NegativeCount,
                s.UpdatedAt,
                decayedStrength = BehavioralSignalService.DecayedStrength(s),
            }),
        });
    }

    /// <summary>
    /// Explicitly suppress a topic. Unlike event-driven feedback this is recorded against every
    /// source that already has a row for the term, so an explicit rejection overrides inference
    /// instead of sitting alongside it.
    /// </summary>
    [HttpDelete("signals")]
    public async Task<IActionResult> RemoveSignalAsync([FromQuery] string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return BadRequest(new { error = "A signal 'term' is required." });

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var trimmed = term.Trim();
        var rows = (await _signals.GetSignalsAsync(profile.Id))
            .Where(s => s.Term.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var row in rows)
            await _signals.RecordAsync(profile.Id, row.Term, BehavioralAction.RemoveInterest, row.Source);

        return NoContent();
    }

    [HttpPut]
    public async Task<IActionResult> UpdateAsync([FromBody] UpdateProfileRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var previousInterests = profile.Interests.ToList();
        Apply(profile, request);
        await _profiles.SaveProfileAsync(profile);
        await SyncDeclaredSignalsAsync(profile.Id, previousInterests, profile.Interests);
        return Ok(profile);
    }

    public sealed class CreateProfileRequest
    {
        public PersonaType Persona { get; set; }
        public string? Label { get; set; }
    }

    /// <summary>
    /// Every profile on this account, and which one is in use.
    /// </summary>
    /// <remarks>
    /// A student who graduates should not have to overwrite themselves. Each profile carries its
    /// own path, feed, saved items and history — that was already true of the data model, since
    /// everything is keyed by profile id; there was simply no way to own a second one.
    /// </remarks>
    public sealed class PersonaliseRequest
    {
        public List<string> Interests { get; set; } = [];
        public List<string> Goals { get; set; } = [];

        /// <summary>Box 3: what is in the reader's way.</summary>
        public List<string> Challenges { get; set; } = [];

        /// <summary>
        /// The older box 3. No longer asked for; accepted so an older client still saves what it
        /// collected rather than having the answer silently dropped.
        /// </summary>
        public List<string> OpportunityPreferences { get; set; } = [];

        public PersonaType? Persona { get; set; }
    }

    /// <summary>
    /// The three boxes: what you follow, what you want, and what is in your way.
    /// </summary>
    /// <remarks>
    /// Asked at the point of use rather than at signup. Before anyone has seen Radar, a
    /// questionnaire is answered carelessly or abandoned; once someone is looking at a feed that
    /// is not theirs, the question answers itself.
    ///
    /// Interests and goals are both required, because the feed is built on them and without
    /// either there is nothing to build from. Challenges are optional — somebody who does not
    /// want to name an obstacle should still get a feed — but they are the box that changes what
    /// the feed is for. A goal says where a reader is going; a challenge says what is stopping
    /// them, and "become a data analyst" and "I have no portfolio to show" want completely
    /// different material from the same corpus.
    ///
    /// The third box used to ask which kinds of opportunity to watch for. Those are worked out
    /// from the goals and challenges now instead of asked for.
    /// </remarks>
    [HttpPost("personalise")]
    public async Task<IActionResult> PersonaliseAsync([FromBody] PersonaliseRequest request, CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var interests = Clean(request.Interests);
        var goals = Clean(request.Goals);

        if (interests.Count == 0 || goals.Count == 0)
        {
            return BadRequest(new
            {
                error = "Radar needs at least one interest and one goal to build a feed that is yours.",
            });
        }

        var previousInterests = profile.Interests.ToList();

        profile.Interests = interests;
        profile.Goals = goals;
        // Into Problems, which is the field this has always been. See UserProfile.Problems.
        profile.Problems = Clean(request.Challenges);

        // Only when an older client sent some. Overwriting a stored preference with the empty
        // list a current client sends would discard an answer somebody gave.
        var opportunityPreferences = Clean(request.OpportunityPreferences);
        if (opportunityPreferences.Count > 0) profile.OpportunityPreferences = opportunityPreferences;

        // The goal a learning path is built around is one of them — the first, until they say
        // otherwise. The list drives the feed; this one drives Learn.
        if (string.IsNullOrWhiteSpace(profile.PrimaryGoal)) profile.PrimaryGoal = goals[0];

        if (request.Persona.HasValue)
        {
            profile.Persona = request.Persona.Value;
            profile.PersonaChosenAt = DateTime.UtcNow;
        }
        profile.OnboardingComplete = true;

        await _profiles.SaveProfileAsync(profile);
        await SyncDeclaredSignalsAsync(profile.Id, previousInterests, interests);

        return Ok(new
        {
            isPersonalised = profile.IsPersonalised,
            interests = profile.Interests,
            goals = profile.Goals,
            challenges = profile.Problems,
            opportunityPreferences = profile.OpportunityPreferences,
            persona = profile.Persona.ToString(),
        });
    }

    [HttpGet("profiles")]
    public async Task<IActionResult> GetProfilesAsync()
    {
        var current = await _profiles.GetCurrentUserAsync();
        var all = await _profiles.GetProfilesAsync();

        return Ok(new
        {
            activeProfileId = current?.Id,
            profiles = all.Select(p => new
            {
                id = p.Id,
                label = string.IsNullOrWhiteSpace(p.Label) ? p.Persona.ToString() : p.Label,
                persona = p.Persona.ToString(),
                primaryGoal = p.PrimaryGoal,
                interests = p.Interests,
                onboardingComplete = p.OnboardingComplete,
                createdAt = p.CreatedAt,
                isActive = p.Id == current?.Id,
            }),
        });
    }

    [HttpPost("profiles")]
    public async Task<IActionResult> CreateProfileAsync([FromBody] CreateProfileRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var created = await _profiles.CreateProfileAsync(request.Persona, request.Label, ct);
        if (created is null) return NotFound(new { error = "Profile not found" });

        // Returned with onboardingComplete false, so the client knows to ask the two questions
        // for this persona rather than assuming the previous profile's answers carry over.
        return Ok(new { id = created.Id, label = created.Label, persona = created.Persona.ToString(), onboardingComplete = false });
    }

    [HttpPost("profiles/{profileId}/activate")]
    public async Task<IActionResult> ActivateProfileAsync(string profileId, CancellationToken ct = default)
    {
        var switched = await _profiles.ActivateProfileAsync(profileId, ct);
        return switched ? NoContent() : NotFound(new { error = "No such profile on this account." });
    }

    /// <summary>
    /// The three questions this reader is asked, and whether they have picked a category yet.
    /// </summary>
    /// <remarks>
    /// Served rather than held in the SPA, so both ends agree on what was asked. The answer text
    /// is the stored value and the matching runs on it, so a client carrying its own copy of the
    /// options can drift from the server that decides what those answers mean - silently, and in
    /// a way no error would show.
    ///
    /// <c>persona</c> is accepted because the category question comes first: a reader choosing
    /// "Graduate" needs the graduate questions before anything has been saved. Without it the
    /// client would have to store a persona it is still asking about.
    ///
    /// <c>hasChosenCategory</c> is the honest signal rather than the persona itself. PersonaType
    /// is an enum, so its default is a real value - Student - and every account reads as one
    /// until it says otherwise. A client trusting the value alone would skip the question for
    /// everybody who had never been asked it.
    /// </remarks>
    [HttpGet("onboarding-questions")]
    public async Task<IActionResult> GetOnboardingQuestionsAsync([FromQuery] string? persona = null)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var asked = profile.Persona;

        if (!string.IsNullOrWhiteSpace(persona))
        {
            // IsDefined as well as TryParse. TryParse accepts a number and does not range-check,
            // so "99" parsed happily into (PersonaType)99 and came back as a real answer - and a
            // client echoing that into the profile would store it, at which point
            // HasChosenCategory reads true because 99 is not the default, the category question
            // is never asked again, and every switch on PersonaType silently takes its fallback
            // arm for ever. TryParse also honours comma lists, so "Student,Graduate" returned
            // Graduate.
            if (!Enum.TryParse<PersonaType>(persona, ignoreCase: true, out var requested)
                || !Enum.IsDefined(requested))
            {
                return BadRequest(new { error = $"Unknown category '{persona}'." });
            }

            asked = requested;
        }

        return Ok(new
        {
            persona = asked.ToString(),
            hasChosenCategory = profile.HasChosenCategory,
            onboardingComplete = profile.OnboardingComplete,
            questions = OnboardingQuestions.For(asked),
        });
    }

    /// <summary>Answers to the three questions.</summary>
    /// <param name="Persona">
    /// The category the questions were answered under, so the server validates against the same
    /// set it served rather than against whatever the profile says now.
    /// </param>
    /// <param name="Answers">
    /// Keyed by the field id the question was served under - interests, skills, goals,
    /// qualification, level - so what is stored is tied to what was asked rather than to the
    /// order a client happened to send.
    /// </param>
    public sealed record OnboardingAnswersRequest(
        string? Persona,
        Dictionary<string, List<string>>? Answers);

    /// <summary>
    /// Stores the answers to the three questions.
    /// </summary>
    /// <remarks>
    /// Separate from onboarding-complete, which predates these questions and carries a different
    /// and partly incompatible set of fields: interests, goals and challenges, where the three
    /// questions ask interests, capabilities and aspirations. Capabilities had no transport at
    /// all, so the half of the engine that decides eligibility could never be told anything.
    ///
    /// Every answer is checked against the options actually offered for that persona. Not for
    /// tidiness: the answer text <em>is</em> the term the feed is selected by, so an endpoint
    /// that accepted free text would let a caller put anything into the matching - and a typo
    /// would silently become a search nobody meant. If it was not offered, it was not asked, and
    /// Radar does not record an answer to a question it did not put.
    /// </remarks>
    [HttpPost("onboarding-answers")]
    public async Task<IActionResult> SaveOnboardingAnswersAsync([FromBody] OnboardingAnswersRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var persona = profile.Persona;
        if (!string.IsNullOrWhiteSpace(request.Persona))
        {
            if (!Enum.TryParse<PersonaType>(request.Persona, ignoreCase: true, out var chosen)
                || !Enum.IsDefined(chosen))
            {
                return BadRequest(new { error = $"Unknown category '{request.Persona}'." });
            }

            persona = chosen;
        }

        if (request.Answers is not { Count: > 0 })
            return BadRequest(new { error = "No answers were sent." });

        var questions = OnboardingQuestions.For(persona);
        var fields = questions
            .SelectMany(question => question.Fields)
            .ToDictionary(field => field.Id, StringComparer.OrdinalIgnoreCase);

        var accepted = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (fieldId, chosen) in request.Answers)
        {
            if (!fields.TryGetValue(fieldId, out var field))
                return BadRequest(new { error = $"'{fieldId}' is not one of the questions asked." });

            var picked = Clean(chosen ?? []);

            if (picked.Count > field.MostSelections)
            {
                return BadRequest(new
                {
                    error = $"'{fieldId}' allows up to {field.MostSelections} choices; {picked.Count} were sent.",
                });
            }

            var offered = field.Options.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = picked.Where(choice => !offered.Contains(choice)).ToList();

            if (unknown.Count > 0)
            {
                return BadRequest(new
                {
                    error = $"'{fieldId}' was not offered: {string.Join(", ", unknown)}",
                });
            }

            // Stored in the wording the question used, not the wording the client sent. The
            // offered check is case-insensitive, so "technology & ai" was accepted and persisted
            // verbatim - and the option text is the value, so two people who picked the same
            // thing ended up with different strings in the database.
            accepted[fieldId] = [.. picked.Select(choice =>
                field.Options.First(option => option.Equals(choice, StringComparison.OrdinalIgnoreCase)))];
        }

        var previousInterests = profile.Interests.ToList();

        OnboardingAnswers.Read(profile, accepted);

        // Only stamped when a category was actually stated. The date is what makes
        // HasChosenCategory honest - Persona is an enum whose default is Student, so every
        // account reads as one until it says otherwise, and the date is the only record of
        // whether anybody asked.
        //
        // Stamping it unconditionally invented that record: answering the three questions
        // without naming a category marked the default as chosen, after which the feed starts
        // saying "it suits where you are now" about a stage nobody stated, and the category
        // question is never put again.
        if (!string.IsNullOrWhiteSpace(request.Persona))
        {
            profile.Persona = persona;
            profile.PersonaChosenAt ??= DateTime.UtcNow;
        }

        // The first goal drives the learning path, which is built around one at a time. Only when
        // nothing has been set, so this never overwrites a path somebody is partway through.
        if (string.IsNullOrWhiteSpace(profile.PrimaryGoal) && profile.Goals.Count > 0)
            profile.PrimaryGoal = profile.Goals[0];

        profile.OnboardingComplete = true;

        await _profiles.SaveProfileAsync(profile);
        await SyncDeclaredSignalsAsync(profile.Id, previousInterests, profile.Interests);

        return Ok(new
        {
            persona = profile.Persona.ToString(),
            isPersonalised = profile.IsPersonalised,
            interests = profile.Interests,
            capabilities = profile.Capabilities,
            goals = profile.Goals,
            qualification = profile.Qualification?.ToString(),
            seniority = profile.SeniorityLevel?.ToString(),
            certifications = profile.Certifications,
            isFounder = profile.IsFounder,
        });
    }

    [HttpPost("onboarding-complete")]
    public async Task<IActionResult> CompleteOnboardingAsync([FromBody] UpdateProfileRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var previousInterests = profile.Interests.ToList();
        Apply(profile, request);
        await _profiles.CompleteOnboardingAsync(profile);
        await SyncDeclaredSignalsAsync(profile.Id, previousInterests, profile.Interests);
        return Ok(profile);
    }

    /// <summary>
    /// Keeps the persisted signal memory in step with the interests the user states directly.
    /// Added interests become strong declared signals; dropped ones are explicitly suppressed so
    /// they stop resurfacing through inference. Removals only touch terms that already exist, so we
    /// never create a signal purely to zero it.
    /// </summary>
    private async Task SyncDeclaredSignalsAsync(string userId, IReadOnlyCollection<string> previous, IReadOnlyCollection<string> current)
    {
        var added   = current.Where(i => !previous.Contains(i, StringComparer.OrdinalIgnoreCase)).ToList();
        var removed = previous.Where(i => !current.Contains(i, StringComparer.OrdinalIgnoreCase)).ToList();
        if (added.Count == 0 && removed.Count == 0) return;

        foreach (var interest in added)
            await _signals.RecordAsync(userId, interest, BehavioralAction.MoreLikeThis, SignalSource.Declared);

        if (removed.Count == 0) return;

        var rows = await _signals.GetSignalsAsync(userId);
        foreach (var interest in removed)
            foreach (var row in rows.Where(r => r.Term.Equals(interest, StringComparison.OrdinalIgnoreCase)))
                await _signals.RecordAsync(userId, row.Term, BehavioralAction.RemoveInterest, row.Source);
    }

    private static void Apply(UserProfile profile, UpdateProfileRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Name))           profile.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.PrimaryGoal))    profile.PrimaryGoal = request.PrimaryGoal.Trim();
        if (request.Region is not null)                          profile.Region = request.Region;
        if (request.City is not null)                            profile.City = request.City;
        if (request.CountryOfResidence is not null)              profile.CountryOfResidence = request.CountryOfResidence.Trim();
        if (request.Nationalities is not null)                   profile.Nationalities = Clean(request.Nationalities);

        // Range-checked rather than trusted. A negative or absurd value is not a fact about
        // anybody, and an eligibility check comparing against one would exclude or include
        // people on the strength of a typo.
        if (request.YearsOfExperience is { } years && years is >= 0 and <= 70)
            profile.YearsOfExperience = years;

        // Bounded the same way, and in the past: a date of birth in the future makes Age
        // negative, and an Age property that can be negative is one every caller has to defend
        // against.
        if (request.DateOfBirth is { } born
            && born < DateTime.UtcNow
            && born > DateTime.UtcNow.AddYears(-120))
        {
            profile.DateOfBirth = born.Date;
        }
        if (request.Interests is not null)
        {
            profile.Interests = request.Interests.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            profile.InterestContexts = (request.InterestContexts ?? profile.InterestContexts)
                .Where(c => profile.Interests.Contains(c.Interest, StringComparer.OrdinalIgnoreCase))
                .GroupBy(c => c.Interest, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            foreach (var interest in profile.Interests.Where(i => profile.InterestContexts.All(c => !c.Interest.Equals(i, StringComparison.OrdinalIgnoreCase))))
                profile.InterestContexts.Add(new UserInterestContext { Interest = interest, Goal = profile.PrimaryGoal });
        }
        else if (request.InterestContexts is not null)
        {
            profile.InterestContexts = request.InterestContexts;
        }
        if (request.DominantInterests is not null)
            profile.DominantInterests = request.DominantInterests.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (request.Problems is not null)
            profile.Problems = Clean(request.Problems);
        if (!string.IsNullOrWhiteSpace(request.CurrentIntent))   profile.CurrentIntent = request.CurrentIntent.Trim();
        if (!string.IsNullOrWhiteSpace(request.TargetRole))      profile.TargetRole = request.TargetRole.Trim();
        if (!string.IsNullOrWhiteSpace(request.TargetIndustry))  profile.TargetIndustry = request.TargetIndustry.Trim();
        if (request.Capabilities is not null)                    profile.Capabilities = Clean(request.Capabilities);
        if (request.OpportunityPreferences is not null)          profile.OpportunityPreferences = Clean(request.OpportunityPreferences);
        if (request.Geography is not null)                       profile.Geography = Clean(request.Geography);
        if (request.DecisionNeeds is not null)                   profile.DecisionNeeds = Clean(request.DecisionNeeds);

        profile.InterestPaths = InterestPathService.Build(profile);
        if (request.PersonaDetails is not null)                  profile.PersonaDetails = request.PersonaDetails;
        if (request.Notifications is not null)                   profile.Notifications = request.Notifications;

        if (Enum.TryParse<PersonaType>(request.Persona, ignoreCase: true, out var persona)
            && persona != profile.Persona)
        {
            // Changing persona changes which questions Radar should have asked. A student and a
            // working professional are asked different things, and their answers drive the roadmap,
            // the home screen and matching — so the old persona's answers are not merely stale,
            // they are about someone else.
            //
            // Reopening onboarding is what the user expected and did not get: they switched persona
            // and nothing changed, because OnboardingComplete stayed true and the router never
            // re-gated them.
            profile.Persona = persona;
            profile.PersonaChosenAt = DateTime.UtcNow;
            profile.OnboardingComplete = false;

            // Only discard the old persona's answers when this request is not itself supplying new
            // ones. The onboarding flows send persona and personaDetails together, and clearing
            // unconditionally would wipe the answers the user had just given.
            if (request.PersonaDetails is null)
                profile.PersonaDetails = new Dictionary<string, string>();
        }
        else if (Enum.TryParse<PersonaType>(request.Persona, ignoreCase: true, out var samePersona))
        {
            profile.Persona = samePersona;
            // Not reassigned. This branch fires when a request echoes the stored persona back
            // unchanged, which is a save, not a choice - and overwriting here would turn the
            // moment-of-choice into "the last time any client sent a persona".
            profile.PersonaChosenAt ??= DateTime.UtcNow;
        }
    }

    private static List<string> Clean(IEnumerable<string> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v))
              .Select(v => v.Trim())
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .ToList();
}
