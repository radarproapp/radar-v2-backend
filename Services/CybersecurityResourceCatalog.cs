using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Cybersecurity learning resources, built from channels whose handles were verified by loading
/// the channel page and reading back its real title (see LEARNING_CHANNELS.md).
///
/// Two deliberate choices:
///
/// 1. There was no cybersecurity pool at all before this. A cybersecurity goal matched none of the
///    keyword branches in LearningResourceCatalog and fell through to software engineering, while
///    the roadmap template fell through to data analytics — which is how a SOC analyst ended up
///    being told to start with Excel fundamentals.
///
/// 2. Each resource links to a channel topic search rather than a hardcoded video ID. Single videos
///    get deleted and renamed, and a wrong ID is invisible in code review; a channel search URL
///    always lands on that channel's current videos for the topic. Same principle as dropping the
///    fabricated research papers — point at something real.
///
/// Summaries are written in plain English on purpose: short words, short sentences.
/// </summary>
public static class CybersecurityResourceCatalog
{
    private static string Search(string handle, string query) =>
        $"https://www.youtube.com/@{handle}/search?query={Uri.EscapeDataString(query)}";

    public static List<LearningResource> All() =>
    [
        // ── BEGINNER: networking and security basics ──
        Resource(
            id: "cy-beg-01", order: 1, level: "Beginner", role: "Start here",
            title: "How networks actually work: TCP/IP, DNS and HTTP",
            handle: "NetworkChuck", author: "NetworkChuck", query: "networking for beginners", time: "50 minutes",
            why: "You cannot defend a network you do not understand. Everything else in security sits on top of this.",
            build: "You will be able to explain, step by step, what happens when a computer opens a website.",
            practice: "Run a packet capture while you load one website. Find the DNS request and the first HTTP request.",
            quiz: "Networking basics",
            matters: "Most attacks travel over normal looking traffic. If you know what normal looks like, odd traffic stands out.",
            next: "1. Watch the networking basics videos.\n2. Install Wireshark.\n3. Capture traffic from one website and find the DNS request."),

        Resource(
            id: "cy-beg-02", order: 2, level: "Beginner", role: "Next",
            title: "Core security ideas: the CIA triad and how attacks happen",
            handle: "professormesser", author: "Professor Messer", query: "Security+ CIA triad", time: "40 minutes",
            why: "These are the words everyone in security uses. You need them before the rest makes sense.",
            build: "You will be able to sort any incident into confidentiality, integrity or availability.",
            practice: "Take three news stories about breaches. Say which part of the triad each one broke.",
            quiz: "Core security principles",
            matters: "Interviewers ask this. More importantly, it is how you decide what to protect first.",
            next: "1. Watch the core principles videos.\n2. Write your own one line definition of each term.\n3. Sort three real breaches."),

        Resource(
            id: "cy-beg-03", order: 3, level: "Beginner", role: "Then",
            title: "Linux and Windows basics for people who defend systems",
            handle: "davidbombal", author: "David Bombal", query: "Linux for beginners", time: "55 minutes",
            why: "Logs, alerts and evidence all live on these systems. You need to move around them without help.",
            build: "You will be comfortable reading logs and checking running processes on both systems.",
            practice: "On a test machine, list running processes, then find the log file that records logins.",
            quiz: "Operating system basics",
            matters: "Almost every alert you investigate ends with you reading a log on a real machine.",
            next: "1. Set up a free Linux virtual machine.\n2. Learn ten basic commands.\n3. Find the login log and read it."),

        // ── INTERMEDIATE: defending and monitoring ──
        Resource(
            id: "cy-int-01", order: 1, level: "Intermediate", role: "Start here",
            title: "Build your own SIEM lab and learn the SOC analyst workflow",
            handle: "MyDFIR", author: "MyDFIR", query: "SOC analyst SIEM home lab", time: "60 minutes",
            why: "A SIEM is where a SOC analyst spends the day. Building one teaches you more than reading about it.",
            build: "A working home lab that collects logs and raises alerts you can investigate.",
            practice: "Send test logs into your lab. Write one rule that fires when a login fails five times.",
            quiz: "SIEM fundamentals",
            matters: "This is the single best thing you can show an employer for an entry level SOC job.",
            next: "1. Follow the home lab series.\n2. Get logs flowing in.\n3. Write one alert rule and make it fire."),

        Resource(
            id: "cy-int-02", order: 2, level: "Intermediate", role: "Next",
            title: "Work real alerts: triage and incident response practice",
            handle: "LetsDefend", author: "LetsDefend", query: "SOC analyst alert triage", time: "50 minutes",
            why: "Knowing the theory is not the same as deciding whether an alert is real. That takes repetition.",
            build: "A habit of working an alert the same way every time: what happened, is it real, what next.",
            practice: "Work three alerts end to end. Write a short note for each saying what you decided and why.",
            quiz: "Alert triage and incident response",
            matters: "Employers hire for judgement under time pressure, not for memorised definitions.",
            next: "1. Work three alerts.\n2. Write your decision and reason for each.\n3. Compare them with the official answer."),

        Resource(
            id: "cy-int-03", order: 3, level: "Intermediate", role: "Then",
            title: "Firewalls, IDS and network segmentation in practice",
            handle: "HackerSploit", author: "HackerSploit", query: "firewall IDS network security", time: "45 minutes",
            why: "These are the controls you will be asked to check, tune and explain.",
            build: "You will be able to say why traffic was blocked and whether the rule was right.",
            practice: "Set a firewall rule in your lab, break it on purpose, and read what the log says.",
            quiz: "Network defence controls",
            matters: "Most real incidents involve a control that was missing, too loose, or switched off.",
            next: "1. Add a firewall rule in your lab.\n2. Trigger it.\n3. Find the matching log line."),

        // ── ADVANCED: hunting and analysis ──
        Resource(
            id: "cy-adv-01", order: 1, level: "Advanced", role: "Start here",
            title: "MITRE ATT&CK and threat hunting you can repeat",
            handle: "_JohnHammond", author: "John Hammond", query: "threat hunting MITRE ATT&CK", time: "55 minutes",
            why: "ATT&CK gives you a shared map of attacker behaviour, so hunting follows a plan instead of a hunch.",
            build: "One written hunt: a question, the data you checked, and what you found.",
            practice: "Pick one ATT&CK technique. Hunt for it in your lab logs. Write the result down, even if it is nothing.",
            quiz: "Threat hunting and ATT&CK",
            matters: "Hunting separates an analyst who waits for alerts from one who finds what the alerts missed.",
            next: "1. Pick one ATT&CK technique.\n2. Write the hunt question.\n3. Search your logs and record the answer."),

        Resource(
            id: "cy-adv-02", order: 2, level: "Advanced", role: "Next",
            title: "Windows forensics and incident response",
            handle: "13cubed", author: "13Cubed", query: "Windows forensics incident response", time: "60 minutes",
            why: "When something real happens, you have to prove what the attacker did. That is forensics.",
            build: "The ability to rebuild a timeline of events from the evidence on a machine.",
            practice: "Take a sample memory image. Build a timeline of what ran and when.",
            quiz: "Digital forensics basics",
            matters: "Incident reports are only trusted if the evidence behind them holds up.",
            next: "1. Download a practice image.\n2. Build a timeline.\n3. Write three sentences on what happened."),

        Resource(
            id: "cy-adv-03", order: 3, level: "Advanced", role: "Then",
            title: "How attacks really work underneath",
            handle: "LiveOverflow", author: "LiveOverflow", query: "how exploits work", time: "50 minutes",
            why: "Understanding the attacker side makes your detections far better than copying rules.",
            build: "A clear mental model of why an attack works, not just that it does.",
            practice: "Follow one exploit explanation all the way through. Write down what the defender would have seen.",
            quiz: "Attack techniques",
            matters: "You can only detect reliably what you actually understand.",
            next: "1. Watch one exploit breakdown.\n2. Write what logs it would leave.\n3. Turn that into a detection idea."),

        // ── APPLICATION: portfolio and interview ──
        Resource(
            id: "cy-app-01", order: 1, level: "Application", role: "Start here",
            title: "Full machine walkthroughs you can follow and repeat",
            handle: "ippsec", author: "IppSec", query: "HackTheBox walkthrough", time: "60 minutes",
            why: "Watching a skilled person work end to end shows you the method, not just the answer.",
            build: "Your own write up of one machine you solved, start to finish.",
            practice: "Solve one easy machine yourself. Write it up as if a colleague will read it.",
            quiz: "Practical security testing",
            matters: "A public write up proves you can do the work. It beats a certificate with nothing behind it.",
            next: "1. Pick one easy machine.\n2. Solve it.\n3. Publish a clear write up."),

        Resource(
            id: "cy-app-02", order: 2, level: "Application", role: "Next",
            title: "Practical ethical hacking, start to finish",
            handle: "TCMSecurityAcademy", author: "The Cyber Mentors", query: "practical ethical hacking", time: "55 minutes",
            why: "It ties the separate skills together into one job you could actually be paid to do.",
            build: "A short assessment report: what you tested, what you found, what you would fix.",
            practice: "Test one machine in your lab and write a one page report with three findings.",
            quiz: "Security assessment reporting",
            matters: "Writing findings clearly is most of the job. Many good testers lose work on the report.",
            next: "1. Test one lab machine.\n2. Write three findings.\n3. Add a fix for each."),

        Resource(
            id: "cy-app-03", order: 3, level: "Application", role: "Then",
            title: "Getting the first cybersecurity job",
            handle: "InfoSecPat", author: "InfoSec Pat", query: "entry level cybersecurity job", time: "40 minutes",
            why: "The skills matter, but so does knowing which roles to target and how to talk about your lab.",
            build: "A CV, and a short story about your home lab that you can tell in an interview.",
            practice: "Rewrite your CV so each line shows something you built or found, not a course you took.",
            quiz: "Career preparation",
            matters: "Most entry level candidates fail on explaining their own work, not on technical knowledge.",
            next: "1. Rewrite your CV around what you built.\n2. Write your lab story in five sentences.\n3. Practise saying it out loud."),
    ];

    private static LearningResource Resource(
        string id, int order, string level, string role, string title, string handle, string author,
        string query, string time, string why, string build, string practice, string quiz,
        string matters, string next) =>
        new()
        {
            Id = id,
            Order = order,
            Level = level,
            SequenceRole = role,
            EstimatedTime = time,
            Title = title,
            Url = Search(handle, query),
            Source = $"YouTube · {author}",
            ChannelOrAuthor = author,
            Duration = time,
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = why,
            WhatYouWillBuild = build,
            PracticeExerciseTitle = $"Practice: {title}",
            PracticeExercise = practice,
            QuizTopic = quiz,
            WhyItMatters = matters,
            NextMove = next,
        };
}
