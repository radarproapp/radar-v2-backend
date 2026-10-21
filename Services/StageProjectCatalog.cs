using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Stage projects per domain, held as data rather than as branches in code.
///
/// Cybersecurity is one row here, not a special case. The earlier mistake — a keyword branch in the
/// roadmap builder, another in the resource catalogue, another in the personaliser — is how a
/// cybersecurity learner ended up on the data-analytics path in the first place: every new domain
/// needed someone to remember three separate `if` statements.
///
/// Adding a domain means adding one entry. Anything not listed still gets real, specific projects
/// built from the learner's own subject, rather than being quietly handed another domain's
/// curriculum.
/// </summary>
public static class StageProjectCatalog
{
    private sealed record Domain(string[] Keywords, Func<string, StageProject>[] Stages);

    private static readonly string[] StageOrder = ["beginner", "intermediate", "advanced", "application"];

    private static readonly Domain[] Domains =
    [
        new(
            DomainVocabulary.For("cyber"),
            [
                _ => Project("Map your own network",
                    "A one page diagram of your home network, with every device and open port listed.",
                    "It shows you can find out what is really on a network instead of guessing.",
                    "2 hours",
                    ["Scan your home network and list every device you find.",
                     "Draw how they connect to each other and to the internet.",
                     "Write one line per device saying what it is and whether it should be there."]),
                _ => Project("Build a small SIEM and catch one attack",
                    "A working log collector with one alert rule that fires on a real test attack.",
                    "This is the daily work of a SOC analyst, done end to end by you.",
                    "6 hours",
                    ["Set up a free SIEM in a virtual machine.",
                     "Send Windows and Linux logs into it.",
                     "Write one rule for repeated failed logins.",
                     "Trigger it on purpose and screenshot the alert."]),
                _ => Project("Write a detection rule from an attack technique",
                    "One detection rule, plus a short note on what it catches and what it misses.",
                    "Writing detections, not just reading alerts, is what moves you past entry level.",
                    "4 hours",
                    ["Pick one MITRE ATT&CK technique.",
                     "Run it safely in your lab.",
                     "Find it in your logs and write a rule that catches it.",
                     "Write down how an attacker could get around your rule."]),
                _ => Project("Write a full incident report",
                    "A short report: what happened, how you found it, what you did, what to fix.",
                    "Employers judge analysts on their write ups. This is the piece you show them.",
                    "4 hours",
                    ["Take one incident from your lab.",
                     "Build a timeline of what happened and when.",
                     "Explain how you found it and what you checked.",
                     "List three fixes that would stop it happening again."]),
            ]),

        new(
            DomainVocabulary.For("data"),
            [
                _ => Project("Clean one messy dataset",
                    "A tidy spreadsheet, plus a note on every change you made and why.",
                    "Most real analysis work is cleaning. Showing your decisions is what proves care.",
                    "2 hours",
                    ["Find a public dataset with obvious problems.",
                     "Fix the duplicates, blanks and wrong types.",
                     "Write down each change and the reason for it."]),
                _ => Project("Answer a real question with SQL",
                    "A short report answering one business question, with the queries you used.",
                    "It shows you can go from a vague question to an answer people can act on.",
                    "4 hours",
                    ["Pick one question a manager might ask.",
                     "Write the queries that answer it.",
                     "Explain the answer in plain words, with one chart."]),
                _ => Project("Build a dashboard people would actually use",
                    "A working dashboard with filters, and a note on what decision it supports.",
                    "Anyone can make charts. Making a dashboard someone uses weekly is the skill.",
                    "6 hours",
                    ["Choose one decision the dashboard should support.",
                     "Build only the views that help that decision.",
                     "Give it to someone and write down what confused them."]),
                _ => Project("Do a full analysis case study",
                    "An end to end case study: question, data, method, finding, recommendation.",
                    "This is the piece that gets you through an analyst interview.",
                    "8 hours",
                    ["Pick a question that matters to a real organisation.",
                     "Do the full analysis.",
                     "Write the recommendation in one page a busy person can read."]),
            ]),

        new(
            DomainVocabulary.For("software"),
            [
                _ => Project("Build and ship one small program",
                    "A working program anyone can run, with a clear README.",
                    "Shipping something that runs on someone else's machine is the first real milestone.",
                    "3 hours",
                    ["Pick one small, useful thing to build.",
                     "Make it work.",
                     "Write a README so a stranger can run it."]),
                _ => Project("Add tests and handle the errors",
                    "The same project, now with tests and sensible behaviour when things go wrong.",
                    "Working code is the easy half. Code that fails well is what teams pay for.",
                    "4 hours",
                    ["Write tests for the main paths.",
                     "Break it on purpose and fix how it fails.",
                     "Make the error messages useful."]),
                _ => Project("Build something with a real backend",
                    "An app with a database, an API and a deployed URL.",
                    "It proves you can connect the parts, which is most of the job.",
                    "8 hours",
                    ["Design the data model first.",
                     "Build the API.",
                     "Deploy it somewhere public."]),
                _ => Project("Publish a project others can read",
                    "A public repository with clean code, tests, and a written explanation of your choices.",
                    "Interviewers read your code and your reasoning. Give them both.",
                    "6 hours",
                    ["Tidy the code so it reads well.",
                     "Write why you made the main design choices.",
                     "Publish it and ask someone to review it."]),
            ]),
    ];

    /// <summary>
    /// The project for one stage of one subject. Falls back to a real, specific project built from
    /// the learner's own subject rather than borrowing another domain's work.
    /// </summary>
    public static StageProject For(string stage, string subject, string? capability)
    {
        var index = Math.Max(0, Array.IndexOf(StageOrder, (stage ?? string.Empty).ToLowerInvariant()));
        var haystack = $"{subject} {capability}".ToLowerInvariant();

        var domain = Domains.FirstOrDefault(d => d.Keywords.Any(haystack.Contains));
        if (domain is not null) return domain.Stages[index](subject);

        return GenericProject(index, subject, capability);
    }

    private static StageProject GenericProject(int stageIndex, string subject, string? capability)
    {
        var topic = SubjectPhrase.Lower(subject);
        var artifact = InterestPersonalization.PortfolioArtifactFor(
            string.IsNullOrWhiteSpace(capability) ? topic : capability, topic);

        return stageIndex switch
        {
            0 => Project($"Start small in {topic}",
                $"One small finished piece of work in {topic}, however rough.",
                "Finishing something small beats half-reading three courses.",
                "2 hours",
                [$"Pick the smallest real task in {topic} you could finish today.",
                 "Do it end to end.",
                 "Write down what was harder than you expected."]),
            1 => Project($"Solve a real problem in {topic}",
                $"A worked solution to a genuine {topic} problem, with your reasoning written down.",
                "Showing your reasoning is what separates you from someone who copied a tutorial.",
                "4 hours",
                [$"Find a real problem in {topic} that someone actually has.",
                 "Solve it using what this stage taught you.",
                 "Write down why you chose your approach."]),
            2 => Project($"Go deep on the hardest part of {topic}",
                $"A piece of work that handles the difficult edge of {topic}, not just the simple case.",
                "Depth in one area is more hireable than a shallow pass over everything.",
                "6 hours",
                [$"Pick the part of {topic} you find hardest.",
                 "Build something that depends on getting it right.",
                 "Explain the part most people get wrong."]),
            _ => Project($"Build your portfolio piece in {topic}",
                $"You will produce {artifact}.",
                "Finished work is the only proof you can do this, not just that you studied it.",
                "8 hours",
                [$"Pick one real problem in {topic}.",
                 "Build the full thing, not a demo.",
                 "Write it up so someone hiring can judge it in two minutes."]),
        };
    }

    private static StageProject Project(string title, string deliverable, string why, string time, List<string> steps) =>
        new()
        {
            Title = title,
            Deliverable = deliverable,
            WhyItProvesCapability = why,
            EstimatedTime = time,
            Steps = steps,
        };
}
