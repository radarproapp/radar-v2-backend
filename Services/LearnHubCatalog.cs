using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Specialized resource and Knowledge Graph catalog for the Radar Learn Hub.
/// Provides multi-format learning resources (Videos, Podcasts, Papers, Articles, Projects, Developments)
/// specifically targeted at the user's primary skill and 1–3 sub-interests.
/// </summary>
public static class LearnHubCatalog
{
    public static LearnHubPayload GetPayload(string skillOrGoal, string? stage = null, string? subInterest = null)
    {
        var normalized = $"{skillOrGoal}".ToLowerInvariant();

        if (normalized.Contains("cyber") || normalized.Contains("security") || normalized.Contains("infosec") || normalized.Contains("hack"))
            return BuildCybersecurityPayload(stage, subInterest);

        if (normalized.Contains("data") || normalized.Contains("analytics") || normalized.Contains("sql") || normalized.Contains("bi"))
            return BuildDataAnalyticsPayload(stage, subInterest);

        if (normalized.Contains("ai") || normalized.Contains("machine learning") || normalized.Contains("artificial") || normalized.Contains("llm"))
            return BuildAiPayload(stage, subInterest);

        // Fallback to Cybersecurity if requested or default
        return BuildCybersecurityPayload(stage, subInterest);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. CYBERSECURITY LEARN HUB & KNOWLEDGE GRAPH
    // ─────────────────────────────────────────────────────────────────────────

    private static LearnHubPayload BuildCybersecurityPayload(string? stage, string? subInterest)
    {
        var currentStage = !string.IsNullOrWhiteSpace(stage) ? stage : "Beginner";
        var subInterests = new List<string>
        {
            "Network Security",
            "Ethical Hacking & Penetration Testing",
            "Cloud Security & SIEM Threat Detection"
        };

        var activeSub = !string.IsNullOrWhiteSpace(subInterest) && subInterests.Contains(subInterest, StringComparer.OrdinalIgnoreCase)
            ? subInterest
            : "All Sub-Interests";

        // ── Knowledge Graph ──────────────────────────────────────────────────
        // Connected graph: Goal → Skills → Sub-skills → Topics → Projects → Opportunities
        var nodes = new List<KnowledgeGraphNode>
        {
            // Goal Node
            new()
            {
                Id = "goal-cyber",
                Label = "Goal: Become a Cybersecurity Analyst",
                Category = "Goal",
                Status = "Learning",
                ConfidencePercent = 45,
                Summary = "Primary career outcome: protecting enterprise systems, identifying vulnerabilities, and responding to incidents.",
                ConnectedNodeIds = ["skill-cyber-fund", "skill-networking", "skill-defensive-ops"]
            },

            // Required Core Skills
            new()
            {
                Id = "skill-cyber-fund",
                Label = "Cybersecurity Fundamentals",
                Category = "Skill",
                Status = "Mastered",
                ConfidencePercent = 90,
                Stage = "Beginner",
                Summary = "CIA Triad, threat actor profiles, authentication mechanisms, and security policy frameworks (NIST/ISO).",
                ConnectedNodeIds = ["topic-cia", "topic-threat-actors"]
            },
            new()
            {
                Id = "skill-networking",
                Label = "Computer Networking & Protocols",
                Category = "Skill",
                Status = "Mastered",
                ConfidencePercent = 85,
                Stage = "Beginner",
                Summary = "OSI 7-layer model, TCP/IP handshakes, DNS, DHCP, subnetting, and packet structures.",
                ConnectedNodeIds = ["topic-osi", "topic-packets", "sub-net-sec"]
            },
            new()
            {
                Id = "skill-defensive-ops",
                Label = "Security Operations & Defense",
                Category = "Skill",
                Status = "Learning",
                ConfidencePercent = 55,
                Stage = "Intermediate",
                Summary = "Host and network monitoring, firewall rule management, log aggregation, and threat analysis.",
                ConnectedNodeIds = ["sub-cloud-siem", "proj-siem-lab"]
            },

            // Sub-Interest 1: Network Security
            new()
            {
                Id = "sub-net-sec",
                Label = "Network Security",
                Category = "SubInterest",
                Status = "Learning",
                ConfidencePercent = 60,
                Stage = "Intermediate",
                LinkedSubInterest = "Network Security",
                Summary = "Protecting network infrastructure through stateful firewalls, IDS/IPS, segmentation, and VPN encryption.",
                ConnectedNodeIds = ["topic-firewalls", "topic-ids-ips", "proj-home-lab"]
            },
            new()
            {
                Id = "topic-firewalls",
                Label = "Firewalls & Packet Filtering (IPTables/pfSense)",
                Category = "Topic",
                Status = "WeakArea",
                ConfidencePercent = 40,
                Stage = "Intermediate",
                LinkedSubInterest = "Network Security",
                Summary = "Stateful packet inspection, ingress/egress filtering rules, and NAT port forwarding.",
                ConnectedNodeIds = ["proj-home-lab"]
            },
            new()
            {
                Id = "topic-ids-ips",
                Label = "Intrusion Detection & Prevention (Snort/Suricata)",
                Category = "Topic",
                Status = "NextUp",
                ConfidencePercent = 20,
                Stage = "Intermediate",
                LinkedSubInterest = "Network Security",
                Summary = "Signature-based and anomaly-based traffic inspection to alert and drop malicious flows.",
                ConnectedNodeIds = ["topic-firewalls"]
            },

            // Sub-Interest 2: Ethical Hacking & Penetration Testing
            new()
            {
                Id = "sub-eth-hack",
                Label = "Ethical Hacking & Penetration Testing",
                Category = "SubInterest",
                Status = "Planned",
                ConfidencePercent = 30,
                Stage = "Advanced",
                LinkedSubInterest = "Ethical Hacking & Penetration Testing",
                Summary = "Offensive reconnaissance, vulnerability exploitation, web app attacks (OWASP), and privilege escalation.",
                ConnectedNodeIds = ["topic-web-pen", "topic-priv-esc", "proj-owasp-suite"]
            },
            new()
            {
                Id = "topic-web-pen",
                Label = "Web Application Security (OWASP Top 10)",
                Category = "Topic",
                Status = "Planned",
                ConfidencePercent = 25,
                Stage = "Advanced",
                LinkedSubInterest = "Ethical Hacking & Penetration Testing",
                Summary = "SQL injection, Cross-Site Scripting (XSS), Broken Object Level Authorization (BOLA), and SSRF.",
                ConnectedNodeIds = ["proj-owasp-suite"]
            },
            new()
            {
                Id = "topic-priv-esc",
                Label = "Linux & Windows Privilege Escalation",
                Category = "Topic",
                Status = "Planned",
                ConfidencePercent = 15,
                Stage = "Advanced",
                LinkedSubInterest = "Ethical Hacking & Penetration Testing",
                Summary = "Sudo misconfigurations, SUID binaries, kernel exploits, and unquoted service paths.",
                ConnectedNodeIds = ["sub-eth-hack"]
            },

            // Sub-Interest 3: Cloud Security & SIEM
            new()
            {
                Id = "sub-cloud-siem",
                Label = "Cloud Security & SIEM Threat Detection",
                Category = "SubInterest",
                Status = "Learning",
                ConfidencePercent = 50,
                Stage = "Intermediate",
                LinkedSubInterest = "Cloud Security & SIEM Threat Detection",
                Summary = "Centralized log ingestion, correlation rules (Sigma/Splunk), threat hunting, and cloud posture.",
                ConnectedNodeIds = ["topic-siem-rules", "topic-threat-hunt", "proj-siem-lab"]
            },
            new()
            {
                Id = "topic-siem-rules",
                Label = "SIEM Log Ingestion & Sigma Detection Rules",
                Category = "Topic",
                Status = "Learning",
                ConfidencePercent = 50,
                Stage = "Intermediate",
                LinkedSubInterest = "Cloud Security & SIEM Threat Detection",
                Summary = "Parsing Sysmon event logs, auth failures, and building real-time alert correlation queries.",
                ConnectedNodeIds = ["proj-siem-lab"]
            },
            new()
            {
                Id = "topic-threat-hunt",
                Label = "Threat Hunting & MITRE ATT&CK Mapping",
                Category = "Topic",
                Status = "Planned",
                ConfidencePercent = 25,
                Stage = "Advanced",
                LinkedSubInterest = "Cloud Security & SIEM Threat Detection",
                Summary = "Mapping adversary tactics, techniques, and procedures (TTPs) against enterprise matrices.",
                ConnectedNodeIds = ["opp-soc-analyst"]
            },

            // Projects & Portfolios
            new()
            {
                Id = "proj-home-lab",
                Label = "Project: Virtual SOC & Firewall Home Lab",
                Category = "Project",
                Status = "NextUp",
                ConfidencePercent = 35,
                Stage = "Application",
                Summary = "Full virtualized network with pfSense router, Snort IDS, and isolated victim machines.",
                ConnectedNodeIds = ["opp-soc-analyst"]
            },
            new()
            {
                Id = "proj-siem-lab",
                Label = "Project: Elastic/Wazuh SIEM Threat Detection Pipeline",
                Category = "Project",
                Status = "Planned",
                ConfidencePercent = 20,
                Stage = "Application",
                Summary = "Ingesting 100k simulated attack logs, writing Sigma rules, and documenting incident triage reports.",
                ConnectedNodeIds = ["opp-soc-analyst"]
            },

            // Career Opportunities
            new()
            {
                Id = "opp-soc-analyst",
                Label = "Opportunity: SOC Analyst / Cybersecurity Analyst Role",
                Category = "Opportunity",
                Status = "Planned",
                ConfidencePercent = 10,
                Stage = "Application",
                Summary = "Entry-to-mid level roles at financial institutions, fintechs, MSSPs, and tech enterprises.",
                ConnectedNodeIds = []
            }
        };

        var edges = new List<KnowledgeGraphEdge>
        {
            new() { FromId = "goal-cyber", ToId = "skill-cyber-fund", Relationship = "requires" },
            new() { FromId = "goal-cyber", ToId = "skill-networking", Relationship = "requires" },
            new() { FromId = "skill-networking", ToId = "sub-net-sec", Relationship = "leads_to" },
            new() { FromId = "sub-net-sec", ToId = "topic-firewalls", Relationship = "contains" },
            new() { FromId = "sub-net-sec", ToId = "topic-ids-ips", Relationship = "contains" },
            new() { FromId = "topic-firewalls", ToId = "proj-home-lab", Relationship = "applies_to" },
            new() { FromId = "skill-defensive-ops", ToId = "sub-cloud-siem", Relationship = "leads_to" },
            new() { FromId = "sub-cloud-siem", ToId = "topic-siem-rules", Relationship = "contains" },
            new() { FromId = "sub-cloud-siem", ToId = "topic-threat-hunt", Relationship = "contains" },
            new() { FromId = "topic-siem-rules", ToId = "proj-siem-lab", Relationship = "applies_to" },
            new() { FromId = "goal-cyber", ToId = "sub-eth-hack", Relationship = "subskill_of" },
            new() { FromId = "sub-eth-hack", ToId = "topic-web-pen", Relationship = "contains" },
            new() { FromId = "sub-eth-hack", ToId = "topic-priv-esc", Relationship = "contains" },
            new() { FromId = "proj-home-lab", ToId = "opp-soc-analyst", Relationship = "unlocks" },
            new() { FromId = "proj-siem-lab", ToId = "opp-soc-analyst", Relationship = "unlocks" }
        };

        // ── Next Best Action ─────────────────────────────────────────────────
        var nextAction = new NextBestAction
        {
            Title = "Master Firewall Fundamentals & IPTables Packet Filtering",
            Description = "You have completed cybersecurity fundamentals and networking basics. Your biggest current gap is network security. Start with this 35-minute firewall fundamentals tutorial, then complete the practice exercise.",
            WhyNowReason = "Network security is the gateway between fundamental theory and real-world security operations. Mastering firewalls and packet filters unlocks your hands-on SOC lab.",
            EstimatedTime = "35 minutes",
            ResourceId = "hub-cyber-01",
            Stage = "Intermediate",
            SubInterest = "Network Security"
        };

        // ── Multi-Format Curated Resources ───────────────────────────────────
        var resources = new List<HubResourceItem>
        {
            // ── 1. LEARN (Tutorials, courses, how-to videos, technical guides) ──
            new()
            {
                Id = "hub-cyber-01",
                Title = "Firewall Fundamentals, IPTables & Packet Filtering – Hands-on Tutorial",
                PrimarySkill = "Cybersecurity",
                SubInterest = "Network Security",
                Level = "Intermediate",
                Category = HubResourceCategory.Learn,
                Format = HubMediaFormat.Video,
                Url = "https://www.youtube.com/watch?v=oxDAv_D_u8c",
                MediaUrl = "https://www.youtube.com/watch?v=oxDAv_D_u8c",
                ThumbnailUrl = "https://img.youtube.com/vi/oxDAv_D_u8c/hqdefault.jpg",
                Source = "YouTube · NetworkChuck",
                AuthorOrChannel = "NetworkChuck",
                DurationOrReadTime = "35 min",
                RankingScore = 99,
                WhyThisResource = "This tutorial is recommended because you are currently learning network security and have not yet completed the fundamentals of firewalls.",
                WhatYouWillLearn = "How stateful packet inspection works, how to configure Linux IPTables rules from scratch, and how to block unauthorized inbound traffic.",
                StepByStepBreakdown =
                [
                    new() { StepNumber = 1, Title = "Inspect Current Chain Policies", Instruction = "Open Linux terminal, run sudo iptables -L -v -n, and inspect default ACCEPT policies for INPUT, FORWARD, and OUTPUT." },
                    new() { StepNumber = 2, Title = "Permit Loopback & Established Connections", Instruction = "Add stateful rules: allow loopback (lo) and traffic with state ESTABLISHED,RELATED so active SSH sessions don't drop." },
                    new() { StepNumber = 3, Title = "Open Essential Ports & Drop Everything Else", Instruction = "Explicitly whitelist port 22 (SSH) and port 443 (HTTPS), then set default policy for INPUT chain to DROP." },
                    new() { StepNumber = 4, Title = "Persist IPTables Configuration", Instruction = "Install iptables-persistent to ensure custom firewall filtering rules survive system reboots." }
                ],
                PracticeExerciseTitle = "Practice: Lockdown an Ubuntu Server with IPTables",
                PracticeExercise = "Spin up a local Linux VM. Configure IPTables to only allow SSH from your host IP and drop ICMP ping requests. Verify with nmap.",
                ConnectedNodeIds = ["sub-net-sec", "topic-firewalls"],
                WhyItMatters = "Firewalls are the first line of perimeter defense; understanding packet filter logic is essential for every security analyst.",
                YourEdge = "You write precise, minimal firewall rules rather than relying on insecure default configurations.",
                NextMove = "1. Watch the 35-minute walkthrough.\n2. Complete the IPTables practice task.\n3. Verify your ports with nmap."
            },
            new()
            {
                Id = "hub-cyber-02",
                Title = "Wireshark Packet Analysis & Protocol Dissection Guide",
                PrimarySkill = "Cybersecurity",
                SubInterest = "Network Security",
                Level = "Beginner",
                Category = HubResourceCategory.Learn,
                Format = HubMediaFormat.Video,
                Url = "https://www.youtube.com/watch?v=lb1Dw0elw0Q",
                MediaUrl = "https://www.youtube.com/watch?v=lb1Dw0elw0Q",
                ThumbnailUrl = "https://img.youtube.com/vi/lb1Dw0elw0Q/hqdefault.jpg",
                Source = "YouTube · Chris Greer",
                AuthorOrChannel = "Chris Greer",
                DurationOrReadTime = "42 min",
                RankingScore = 96,
                WhyThisResource = "Recommended to master network packet inspection before proceeding to threat detection and intrusion analysis.",
                WhatYouWillLearn = "Capturing live network packets, filtering TCP handshakes, following HTTP/TLS streams, and spotting suspicious beaconing.",
                StepByStepBreakdown =
                [
                    new() { StepNumber = 1, Title = "Selecting Network Interfaces & Capture Filters", Instruction = "Configure capture buffer options and apply pre-capture Berkeley Packet Filters (BPF) to limit noise." },
                    new() { StepNumber = 2, Title = "Display Filtering Syntax Mastery", Instruction = "Use display filters like ip.addr == X, tcp.flags.syn == 1, and http.request.method == POST." },
                    new() { StepNumber = 3, Title = "Following TCP Streams & Extracting Objects", Instruction = "Reassemble complete conversational streams and export transferred files directly from PCAP traces." }
                ],
                PracticeExerciseTitle = "Practice: Analyze a Malicious PCAP File",
                PracticeExercise = "Download the sample malware traffic analysis PCAP. Identify the infected IP address, the C2 server domain, and the downloaded executable hash.",
                ConnectedNodeIds = ["skill-networking", "sub-net-sec"],
                WhyItMatters = "Packet analysis provides ground truth when alerts trigger; logs can be spoofed, but raw packets rarely lie.",
                YourEdge = "You can dissect network anomalies in minutes rather than guessing what happened during an intrusion.",
                NextMove = "1. Open the sample PCAP in Wireshark.\n2. Identify the 3-way handshake.\n3. Take the quiz."
            },

            // ── 2. UNDERSTAND (Articles, podcasts, explainers, interviews) ──
            new()
            {
                Id = "hub-cyber-03",
                Title = "Darknet Diaries: Operation Trojan Shield & The Anom Phone Global Sting",
                PrimarySkill = "Cybersecurity",
                SubInterest = "Ethical Hacking & Penetration Testing",
                Level = "Intermediate",
                Category = HubResourceCategory.Understand,
                Format = HubMediaFormat.AudioPodcast,
                Url = "https://darknetdiaries.com/episode/102/",
                MediaUrl = "https://traffic.megaphone.fm/DARKNET9086609999.mp3",
                ThumbnailUrl = "https://darknetdiaries.com/imgs/ep102.jpg",
                Source = "Podcast · Darknet Diaries",
                AuthorOrChannel = "Jack Rhysider",
                DurationOrReadTime = "52 min listen",
                RankingScore = 98,
                WhyThisResource = "Recommended to understand real-world cryptographic backdoors, adversary opsec failures, and global law enforcement technical operations.",
                PodcastBreakdown = new()
                {
                    KeyIdeas =
                    [
                        "The FBI and Australian Federal Police secretly built and distributed Anom, an encrypted phone company used exclusively by organized crime syndicates.",
                        "Every message sent on Anom was automatically decrypted and carbon-copied to law enforcement servers using a master private key embedded in the firmware.",
                        "Adversaries placed blind trust in a closed-source proprietary platform without verifying cryptographic implementations."
                    ],
                    ImportantPoints =
                    [
                        "Over 27 million messages were intercepted across 300 syndicates in 100+ countries.",
                        "Led to 800+ arrests, 8 tons of cocaine seized, and $48M in illicit currencies recovered.",
                        "Demonstrates that end-to-end encryption is only as secure as the device endpoint and the trust model of the underlying firmware."
                    ],
                    WhyItMatters = "Teaches vital lessons in operational security (OpSec), endpoint verification, and how cryptographic systems can be subverted at the distribution layer.",
                    Takeaways =
                    [
                        "Never trust closed-source security hardware whose source code and cryptographic keys cannot be independently verified.",
                        "Adversaries frequently compromise themselves through convenience and misplaced trust.",
                        "Endpoint compromise completely bypasses transport-layer encryption."
                    ],
                    NextMove = "Reflect on endpoint trust models: write a 1-paragraph summary on why endpoint integrity is foundational to cryptography."
                },
                ConnectedNodeIds = ["goal-cyber", "sub-eth-hack"],
                WhyItMatters = "Provides deep operational understanding of state-level technical capabilities and supply chain subversion.",
                YourEdge = "You evaluate security architectures holistically—accounting for hardware, firmware, and human trust models.",
                NextMove = "Listen to the episode and take notes on the endpoint trust model."
            },
            new()
            {
                Id = "hub-cyber-04",
                Title = "Zero Trust Architecture Explained: Identity as the Modern Perimeter",
                PrimarySkill = "Cybersecurity",
                SubInterest = "Cloud Security & SIEM Threat Detection",
                Level = "Intermediate",
                Category = HubResourceCategory.Understand,
                Format = HubMediaFormat.Article,
                Url = "https://krebsonsecurity.com",
                ThumbnailUrl = "https://img.youtube.com/vi/xxpc-HPKN28/hqdefault.jpg",
                Source = "Krebs on Security · Technical Analysis",
                AuthorOrChannel = "Brian Krebs",
                DurationOrReadTime = "12 min read",
                RankingScore = 92,
                WhyThisResource = "Recommended because modern cloud security replaces traditional castle-and-moat perimeter models with continuous identity verification.",
                WhatHappened = "Major enterprise breaches have driven global adoption of NIST SP 800-207 Zero Trust, mandating that no user or device is trusted by default, even inside the internal network.",
                WhyItMatters = "Attackers who gain a foothold on one workstation can no longer laterally move across flat internal subnets without re-authenticating.",
                YourEdge = "You design cloud architectures that enforce least-privilege role-based access control (RBAC) and mutual TLS everywhere.",
                NextMove = "1. Read the Zero Trust analysis.\n2. Review NIST SP 800-207 architecture principles.\n3. Take the quiz."
            },

            // ── 3. RESEARCH (Academic papers, conference papers, research reports) ──
            new()
            {
                Id = "hub-cyber-05",
                Title = "Evaluating Large Language Models as Autonomous Penetration Testers (ACM CCS 2025)",
                PrimarySkill = "Cybersecurity",
                SubInterest = "Ethical Hacking & Penetration Testing",
                Level = "Advanced",
                Category = HubResourceCategory.Research,
                Format = HubMediaFormat.AcademicPaper,
                Url = "https://arxiv.org/abs/2402.11814",
                Source = "ACM CCS 2025 · Peer-Reviewed Academic Paper",
                AuthorOrChannel = "Stanford & CMU Security Research Group",
                DurationOrReadTime = "25 min study",
                RankingScore = 97,
                WhyThisResource = "Recommended to understand cutting-edge frontier research on autonomous AI agent exploitation capabilities and defensive counter-measures.",
                PaperBreakdown = new()
                {
                    Introduction = "Examines whether modern frontier AI models equipped with tool-calling capabilities can autonomously discover and exploit zero-day and one-day software vulnerabilities without human intervention.",
                    Problem = "Traditional penetration testing is expensive, manual, and slow. If adversaries automate exploit generation using autonomous agents, defensive teams will be overwhelmed by high-velocity attacks.",
                    AimAndObjectives = "To benchmark frontier LLMs against real-world Capture-The-Flag (CTF) environments and vulnerable open-source repositories to measure autonomous exploitation success rates.",
                    Methodology = "Constructed an automated execution sandbox with 150 diverse vulnerability targets (web apps, binary exploitation, privilege escalation). Equipped agents with terminal, Metasploit, and Python tools.",
                    KeyArguments = "Autonomous models excel at multi-step web application vulnerabilities (XSS, SQLi, CSRF) when given structured feedback loops, but struggle with complex heap memory corruption.",
                    Findings = "Agents successfully resolved 73% of web security challenges autonomously when given iterative feedback from execution logs, outperforming junior human security analysts in speed.",
                    Contribution = "Provides the first empirical benchmark for autonomous cyber agents and formalizes defensive guidelines for rate-limiting and behavior-based bot detection.",
                    WhyItMatters = "Security analysts must prepare for automated, agentic vulnerability discovery by deploying automated defense, rapid patch cycles, and AI-assisted triage."
                },
                ConnectedNodeIds = ["sub-eth-hack", "topic-web-pen"],
                WhyItMatters = "Academic papers provide rigorous empirical benchmarks that reveal where attacker capabilities will be 18 to 24 months from now.",
                YourEdge = "You anticipate automated adversarial attack vectors before they become widespread in the wild.",
                NextMove = "1. Read the research paper breakdown.\n2. Note the automated web exploitation findings.\n3. Test an automated scanner."
            },
            new()
            {
                Id = "hub-cyber-06",
                Title = "Mandiant M-Trends 2026: Global Attacker Dwell Times & Living-Off-The-Land Vectors",
                PrimarySkill = "Cybersecurity",
                SubInterest = "Cloud Security & SIEM Threat Detection",
                Level = "Intermediate",
                Category = HubResourceCategory.Research,
                Format = HubMediaFormat.AcademicPaper,
                Url = "https://www.mandiant.com/m-trends",
                Source = "Mandiant / Google Cloud Research Report",
                AuthorOrChannel = "Mandiant Threat Intelligence",
                DurationOrReadTime = "20 min read",
                RankingScore = 95,
                WhyThisResource = "Recommended to benchmark real-world intrusion statistics, median dwell times, and the top attacker techniques mapped to MITRE ATT&CK.",
                WhatHappened = "Mandiant published its annual frontline intelligence report: median global attacker dwell time dropped to 10 days, but living-off-the-land (LotL) techniques surged by 45%.",
                WhyItMatters = "Attackers increasingly use legitimate administrative tools (PowerShell, WMI, certutil) rather than dropping malware binaries, evading signature-based antivirus.",
                YourEdge = "You focus defensive telemetry on behavioral anomalies (e.g. certutil downloading base64 payloads) rather than relying on file hashes.",
                NextMove = "1. Review the Top 10 MITRE techniques in the report.\n2. Write a detection rule for living-off-the-land binaries.\n3. Take the quiz."
            },

            // ── 4. APPLY (Projects, case studies, practical exercises, challenges) ──
            new()
            {
                Id = "hub-cyber-07",
                Title = "Hands-on Project: Build a Virtual SOC & Firewall Home Lab with pfSense & Wazuh",
                PrimarySkill = "Cybersecurity",
                SubInterest = "Network Security",
                Level = "Application",
                Category = HubResourceCategory.Apply,
                Format = HubMediaFormat.ProjectGuide,
                Url = "https://github.com",
                ThumbnailUrl = "https://img.youtube.com/vi/Wz7n9Xy1g8Y/hqdefault.jpg",
                Source = "GitHub · SOC Home Lab Blueprint",
                AuthorOrChannel = "CyberSecurity Open Lab",
                DurationOrReadTime = "60 min build",
                RankingScore = 99,
                WhyThisResource = "This project transforms your theoretical networking knowledge into a tangible, working Security Operations Center on your resume.",
                WhatYouWillLearn = "Setting up a virtual isolated network in VirtualBox/VMware, deploying pfSense router, configuring Snort IDS, and forwarding logs to Wazuh SIEM.",
                StepByStepBreakdown =
                [
                    new() { StepNumber = 1, Title = "Virtual Network Topography Design", Instruction = "Create isolated Host-Only and NAT networks in VirtualBox; assign WAN and LAN virtual interfaces." },
                    new() { StepNumber = 2, Title = "pfSense Router & Firewall Deployment", Instruction = "Install pfSense ISO, configure internal DHCP range (192.168.1.0/24), and set up web management GUI." },
                    new() { StepNumber = 3, Title = "Deploying Wazuh Open-Source SIEM Manager", Instruction = "Spin up an Ubuntu Server instance running Wazuh Manager, Elasticsearch, and Kibana dashboard." },
                    new() { StepNumber = 4, Title = "Simulating Attacks & Verifying Alert Ingestion", Instruction = "Deploy a Windows 10/11 victim VM with Wazuh agent installed; launch hydra brute-force and verify SIEM alert triggers." }
                ],
                PracticeExerciseTitle = "Practice: Complete Your Virtual SOC Lab",
                PracticeExercise = "Deploy the 3 virtual machines. Generate an alert in Wazuh by running an nmap port scan from Kali Linux. Take screenshots of the Kibana alert dashboard for your portfolio.",
                ConnectedNodeIds = ["sub-net-sec", "proj-home-lab", "opp-soc-analyst"],
                WhyItMatters = "Hiring managers interview candidates with home labs with 3x higher preference because it proves hands-on curiosity and execution ability.",
                YourEdge = "You have actually configured network interfaces, analyzed real log files, and simulated intrusions rather than just reading multiple-choice test banks.",
                NextMove = "1. Follow the 4 setup steps.\n2. Verify Wazuh alert triggers.\n3. Request an AI Mentor project review."
            },
            new()
            {
                Id = "hub-cyber-08",
                Title = "Web Application Pen Testing Challenge: OWASP Top 10 Exploitation with Burp Suite",
                PrimarySkill = "Cybersecurity",
                SubInterest = "Ethical Hacking & Penetration Testing",
                Level = "Intermediate",
                Category = HubResourceCategory.Apply,
                Format = HubMediaFormat.ProjectGuide,
                Url = "https://portswigger.net/web-security",
                Source = "PortSwigger Web Security Academy",
                AuthorOrChannel = "PortSwigger",
                DurationOrReadTime = "45 min challenge",
                RankingScore = 96,
                WhyThisResource = "Recommended to learn how web application flaws are identified, exploited responsibly, and patched.",
                WhatYouWillLearn = "Intercepting HTTP traffic with Burp Suite Proxy, fuzzing parameters for SQL injection, and bypassing authentication logic.",
                StepByStepBreakdown =
                [
                    new() { StepNumber = 1, Title = "Configuring Burp Suite Local Proxy", Instruction = "Configure browser proxy settings (127.0.0.1:8080) and install the PortSwigger CA certificate." },
                    new() { StepNumber = 2, Title = "Intercepting & Modifying Login Requests", Instruction = "Capture a POST /login request in Burp Repeater; test single quote (') and boolean logic OR 1=1-- payloads." },
                    new() { StepNumber = 3, Title = "Extracting Database Tables with UNION SELECT", Instruction = "Determine column count and inject UNION SELECT null, username, password FROM users--." }
                ],
                PracticeExerciseTitle = "Practice: Solve the PortSwigger SQLi Lab",
                PracticeExercise = "Complete the PortSwigger SQL Injection lab: exploit the product category filter to retrieve administrator credentials and log in.",
                ConnectedNodeIds = ["sub-eth-hack", "topic-web-pen"],
                WhyItMatters = "Over 60% of external corporate breaches originate from vulnerable public-facing web applications and APIs.",
                YourEdge = "You understand both how attackers craft exploit payloads and how developers should implement parameterized prepared statements.",
                NextMove = "1. Launch Burp Suite.\n2. Complete the SQLi lab.\n3. Submit writeup to AI Mentor."
            },

            // ── 5. STAY CURRENT (New technologies, industry developments, announcements) ──
            new()
            {
                Id = "hub-cyber-09",
                Title = "Critical OpenSSH RegreSSHion Exploit (CVE-2024-6387) & Remediation Playbook",
                PrimarySkill = "Cybersecurity",
                SubInterest = "Network Security",
                Level = "Intermediate",
                Category = HubResourceCategory.StayCurrent,
                Format = HubMediaFormat.Article,
                Url = "https://www.qualys.com/regresshion",
                Source = "Qualys Security Advisory",
                AuthorOrChannel = "Qualys Threat Lab",
                DurationOrReadTime = "10 min read",
                RankingScore = 98,
                WhyThisResource = "High-priority vulnerability advisory affecting millions of Linux enterprise servers worldwide.",
                WhatHappened = "Qualys discovered a remote unauthenticated code execution vulnerability in OpenSSH server (sshd) on glibc-based Linux systems due to a signal handler race condition.",
                WhyItMatters = "Allows full root access over port 22 without requiring valid credentials. Because SSH is exposed on almost every cloud server, rapid patching is critical.",
                YourEdge = "You can audit fleet servers using nmap vulnerability scripts and apply LoginGraceTime 0 workarounds before patches can be deployed.",
                NextMove = "1. Check your Linux server OpenSSH versions.\n2. Apply the mitigation (set LoginGraceTime 0 in sshd_config).\n3. Reload ssh service."
            },
            new()
            {
                Id = "hub-cyber-10",
                Title = "NIST Releases Finalized Post-Quantum Cryptography Standards (FIPS 203, 204, 205)",
                PrimarySkill = "Cybersecurity",
                SubInterest = "Cloud Security & SIEM Threat Detection",
                Level = "Advanced",
                Category = HubResourceCategory.StayCurrent,
                Format = HubMediaFormat.Article,
                Url = "https://www.nist.gov/pqc",
                Source = "NIST Official Announcement",
                AuthorOrChannel = "NIST Computer Security Division",
                DurationOrReadTime = "15 min read",
                RankingScore = 94,
                WhyThisResource = "Historic cryptographic transition: RSA and ECC algorithms will eventually be broken by quantum computers; enterprises are migrating now.",
                WhatHappened = "NIST finalized the primary mathematical standards for post-quantum cryptography: ML-KEM (Kyber) for general encryption, and ML-DSA (Dilithium) for digital signatures.",
                WhyItMatters = "Nation-state adversaries are currently executing 'Store Now, Decrypt Later' operations against encrypted government and banking traffic.",
                YourEdge = "You understand post-quantum key encapsulation mechanisms and can guide organizations through crypto-agility audits.",
                NextMove = "1. Read the NIST FIPS 203 summary.\n2. Inspect how OpenSSL and Cloudflare are deploying Kyber.\n3. Take the checkpoint quiz."
            }
        };

        // Filter resources if a specific sub-interest is selected
        var filteredResources = activeSub == "All Sub-Interests"
            ? resources
            : resources.Where(r => r.SubInterest.Equals(activeSub, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filteredResources.Count == 0) filteredResources = resources;

        var masteredCount = nodes.Count(n => n.Status == "Mastered");
        var learningCount = nodes.Count(n => n.Status == "Learning");
        var weakCount = nodes.Count(n => n.Status == "WeakArea");

        return new LearnHubPayload
        {
            PrimarySkill = "Cybersecurity",
            Goal = "Become a Cybersecurity Analyst",
            CurrentStage = currentStage,
            SubInterests = subInterests,
            ActiveSubInterest = activeSub,
            NextAction = nextAction,
            GraphNodes = nodes,
            GraphEdges = edges,
            Resources = filteredResources,
            TotalMasteredNodes = masteredCount,
            TotalLearningNodes = learningCount,
            TotalWeakNodes = weakCount
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. DATA ANALYTICS PAYLOAD
    // ─────────────────────────────────────────────────────────────────────────

    private static LearnHubPayload BuildDataAnalyticsPayload(string? stage, string? subInterest)
    {
        var currentStage = !string.IsNullOrWhiteSpace(stage) ? stage : "Beginner";
        var subInterests = new List<string>
        {
            "SQL & Relational Databases",
            "Business Intelligence & Power BI",
            "Python Data Science & Machine Learning"
        };

        var activeSub = !string.IsNullOrWhiteSpace(subInterest) && subInterests.Contains(subInterest, StringComparer.OrdinalIgnoreCase)
            ? subInterest
            : "All Sub-Interests";

        var nodes = new List<KnowledgeGraphNode>
        {
            new() { Id = "goal-da", Label = "Goal: Data Analyst Career", Category = "Goal", Status = "Learning", ConfidencePercent = 60, Summary = "Extracting business insights, building dashboards, and presenting findings to leaders." },
            new() { Id = "skill-excel", Label = "Excel & Data Wrangling", Category = "Skill", Status = "Mastered", ConfidencePercent = 90, Stage = "Beginner", Summary = "XLOOKUP, pivot tables, Power Query." },
            new() { Id = "skill-sql", Label = "SQL & Relational Databases", Category = "Skill", Status = "Learning", ConfidencePercent = 65, Stage = "Intermediate", LinkedSubInterest = "SQL & Relational Databases", Summary = "Joins, aggregations, CTEs, and window functions." },
            new() { Id = "skill-bi", Label = "Power BI & Data Storytelling", Category = "Skill", Status = "NextUp", ConfidencePercent = 40, Stage = "Intermediate", LinkedSubInterest = "Business Intelligence & Power BI", Summary = "Star schemas, DAX measures, interactive dashboards." },
            new() { Id = "skill-python", Label = "Python for Analytics", Category = "Skill", Status = "Planned", ConfidencePercent = 20, Stage = "Advanced", LinkedSubInterest = "Python Data Science & Machine Learning", Summary = "Pandas, NumPy, Seaborn, predictive Scikit-Learn." },
            new() { Id = "proj-capstone", Label = "Project: End-to-End Telecom Churn Analysis", Category = "Project", Status = "Planned", ConfidencePercent = 10, Stage = "Application", Summary = "Full SQL, Power BI, and executive memo portfolio piece." },
            new() { Id = "opp-da-role", Label = "Opportunity: Data Analyst Role", Category = "Opportunity", Status = "Planned", ConfidencePercent = 10, Stage = "Application", Summary = "Analyst positions in finance, technology, and consulting." }
        };

        var edges = new List<KnowledgeGraphEdge>
        {
            new() { FromId = "goal-da", ToId = "skill-excel", Relationship = "requires" },
            new() { FromId = "skill-excel", ToId = "skill-sql", Relationship = "leads_to" },
            new() { FromId = "skill-sql", ToId = "skill-bi", Relationship = "leads_to" },
            new() { FromId = "skill-bi", ToId = "skill-python", Relationship = "leads_to" },
            new() { FromId = "skill-bi", ToId = "proj-capstone", Relationship = "applies_to" },
            new() { FromId = "proj-capstone", ToId = "opp-da-role", Relationship = "unlocks" }
        };

        var nextAction = new NextBestAction
        {
            Title = "Master Multi-Table SQL Joins & Window Functions",
            Description = "You have mastered spreadsheet basics. Your biggest current opportunity is querying PostgreSQL databases directly. Start with this 55-minute SQL tutorial, then complete the practice churn query.",
            WhyNowReason = "Over 80% of data analyst job interviews test SQL query proficiency. Mastering relational joins unlocks your intermediate business intelligence projects.",
            EstimatedTime = "55 minutes",
            ResourceId = "hub-da-01",
            Stage = "Intermediate",
            SubInterest = "SQL & Relational Databases"
        };

        var resources = new List<HubResourceItem>
        {
            new()
            {
                Id = "hub-da-01",
                Title = "Complete SQL for Data Analytics – Zero to Hero Query Mastery",
                PrimarySkill = "Data Analytics",
                SubInterest = "SQL & Relational Databases",
                Level = "Intermediate",
                Category = HubResourceCategory.Learn,
                Format = HubMediaFormat.Video,
                Url = "https://www.youtube.com/watch?v=HXV3zeRR3h4",
                MediaUrl = "https://www.youtube.com/watch?v=HXV3zeRR3h4",
                ThumbnailUrl = "https://img.youtube.com/vi/HXV3zeRR3h4/hqdefault.jpg",
                Source = "YouTube · Luke Barousse",
                AuthorOrChannel = "Luke Barousse",
                DurationOrReadTime = "55 min",
                RankingScore = 99,
                WhyThisResource = "This tutorial is recommended because you are currently learning SQL and need to master relational multi-table joins before building dashboards.",
                WhatYouWillLearn = "PostgreSQL schema exploration, multi-table INNER/LEFT joins, CTEs, and window ranking functions.",
                StepByStepBreakdown =
                [
                    new() { StepNumber = 1, Title = "Schema Inspection & Joins", Instruction = "Join users and orders tables on user_id avoiding row duplication." },
                    new() { StepNumber = 2, Title = "Writing Common Table Expressions", Instruction = "Use WITH clauses to compute intermediate monthly retention cohorts." },
                    new() { StepNumber = 3, Title = "Window Functions", Instruction = "Apply DENSE_RANK() and LAG() to calculate month-over-month growth." }
                ],
                PracticeExerciseTitle = "Practice: Compute Customer Retention in SQL",
                PracticeExercise = "Write a query with a CTE that calculates monthly customer cohort retention percentage for an e-commerce platform.",
                ConnectedNodeIds = ["skill-sql", "skill-bi"],
                WhyItMatters = "SQL is the foundational query language of enterprise analytics; every analyst uses it daily.",
                YourEdge = "You write clean, readable CTE queries that execute fast on production tables.",
                NextMove = "1. Watch the video.\n2. Write the cohort query.\n3. Take the quiz."
            },
            new()
            {
                Id = "hub-da-02",
                Title = "Freakonomics Radio: The Power and Perils of Big Data in Decision Making",
                PrimarySkill = "Data Analytics",
                SubInterest = "Business Intelligence & Power BI",
                Level = "Intermediate",
                Category = HubResourceCategory.Understand,
                Format = HubMediaFormat.AudioPodcast,
                Url = "https://freakonomics.com",
                MediaUrl = "https://traffic.megaphone.fm/FREAKONOMICS.mp3",
                ThumbnailUrl = "https://img.youtube.com/vi/xxpc-HPKN28/hqdefault.jpg",
                Source = "Podcast · Freakonomics Radio",
                AuthorOrChannel = "Stephen Dubner",
                DurationOrReadTime = "45 min listen",
                RankingScore = 94,
                WhyThisResource = "Recommended to develop critical thinking and understand why correlation does not equal causation in business data.",
                PodcastBreakdown = new()
                {
                    KeyIdeas =
                    [
                        "Businesses frequently confuse correlated metrics with root causal factors.",
                        "Observational data requires rigorous natural experiments or A/B testing before altering pricing.",
                        "Great analysts ask 'What is the incentive driving this data point?'"
                    ],
                    ImportantPoints =
                    [
                        "Case study: why advertising metrics were inflated by 400% before randomized holdouts.",
                        "The danger of survivorship bias in corporate data warehouses.",
                        "How to communicate statistical uncertainty to confident executives."
                    ],
                    WhyItMatters = "Prevents analysts from making flawed recommendations based on surface-level data patterns.",
                    Takeaways =
                    [
                        "Always check for confounding variables.",
                        "Challenge assumptions before presenting charts.",
                        "Pair quantitative numbers with qualitative user interviews."
                    ],
                    NextMove = "Listen to the episode and write down one example of confounding bias."
                },
                ConnectedNodeIds = ["goal-da", "skill-bi"],
                WhyItMatters = "Elevates your analytical perspective from a spreadsheet technician to a strategic business partner.",
                YourEdge = "You identify hidden biases that other analysts overlook.",
                NextMove = "Listen to the podcast episode."
            }
        };

        var filtered = activeSub == "All Sub-Interests"
            ? resources
            : resources.Where(r => r.SubInterest.Equals(activeSub, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filtered.Count == 0) filtered = resources;

        return new LearnHubPayload
        {
            PrimarySkill = "Data Analytics",
            Goal = "Become a Data Analyst",
            CurrentStage = currentStage,
            SubInterests = subInterests,
            ActiveSubInterest = activeSub,
            NextAction = nextAction,
            GraphNodes = nodes,
            GraphEdges = edges,
            Resources = filtered,
            TotalMasteredNodes = nodes.Count(n => n.Status == "Mastered"),
            TotalLearningNodes = nodes.Count(n => n.Status == "Learning"),
            TotalWeakNodes = nodes.Count(n => n.Status == "WeakArea")
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. AI & MACHINE LEARNING PAYLOAD
    // ─────────────────────────────────────────────────────────────────────────

    private static LearnHubPayload BuildAiPayload(string? stage, string? subInterest)
    {
        var currentStage = !string.IsNullOrWhiteSpace(stage) ? stage : "Beginner";
        var subInterests = new List<string>
        {
            "Foundational Models & PyTorch",
            "Retrieval-Augmented Generation (RAG)",
            "Autonomous Agents & LangGraph"
        };

        var activeSub = !string.IsNullOrWhiteSpace(subInterest) && subInterests.Contains(subInterest, StringComparer.OrdinalIgnoreCase)
            ? subInterest
            : "All Sub-Interests";

        var nodes = new List<KnowledgeGraphNode>
        {
            new() { Id = "goal-ai", Label = "Goal: AI Engineer Career", Category = "Goal", Status = "Learning", ConfidencePercent = 50, Summary = "Building production AI systems, fine-tuning models, and deploying agentic workflows." },
            new() { Id = "skill-python-ml", Label = "Python & Scientific Libraries", Category = "Skill", Status = "Mastered", ConfidencePercent = 90, Stage = "Beginner", Summary = "NumPy, Pandas, Scikit-Learn baselines." },
            new() { Id = "skill-pytorch", Label = "PyTorch & Deep Learning", Category = "Skill", Status = "Learning", ConfidencePercent = 60, Stage = "Intermediate", LinkedSubInterest = "Foundational Models & PyTorch", Summary = "Tensors, autograd, backpropagation, and transformer architectures." },
            new() { Id = "skill-rag", Label = "Production Enterprise RAG", Category = "Skill", Status = "NextUp", ConfidencePercent = 40, Stage = "Intermediate", LinkedSubInterest = "Retrieval-Augmented Generation (RAG)", Summary = "Hybrid search, cross-encoder re-ranking, and citation hygiene." },
            new() { Id = "skill-agents", Label = "Autonomous Agents (LangGraph)", Category = "Skill", Status = "Planned", ConfidencePercent = 25, Stage = "Advanced", LinkedSubInterest = "Autonomous Agents & LangGraph", Summary = "State machines, tool calling, and human-in-the-loop validation." }
        };

        var edges = new List<KnowledgeGraphEdge>
        {
            new() { FromId = "goal-ai", ToId = "skill-python-ml", Relationship = "requires" },
            new() { FromId = "skill-python-ml", ToId = "skill-pytorch", Relationship = "leads_to" },
            new() { FromId = "skill-pytorch", ToId = "skill-rag", Relationship = "leads_to" },
            new() { FromId = "skill-rag", ToId = "skill-agents", Relationship = "leads_to" }
        };

        var nextAction = new NextBestAction
        {
            Title = "Implement Hybrid RAG with Cross-Encoder Re-Ranking",
            Description = "You have completed PyTorch foundations. Your biggest current priority is enterprise RAG. Start with this 52-minute tutorial on combining BM25 keyword search with dense embeddings.",
            WhyNowReason = "Enterprise companies require grounded AI models that cite specific internal PDF paragraphs with zero hallucination.",
            EstimatedTime = "52 minutes",
            ResourceId = "hub-ai-01",
            Stage = "Intermediate",
            SubInterest = "Retrieval-Augmented Generation (RAG)"
        };

        var resources = new List<HubResourceItem>
        {
            new()
            {
                Id = "hub-ai-01",
                Title = "Production RAG with Hybrid Search, Re-ranking & Citations",
                PrimarySkill = "AI & Machine Learning",
                SubInterest = "Retrieval-Augmented Generation (RAG)",
                Level = "Intermediate",
                Category = HubResourceCategory.Learn,
                Format = HubMediaFormat.Video,
                Url = "https://www.youtube.com/watch?v=sVcwVQRHIc8",
                MediaUrl = "https://www.youtube.com/watch?v=sVcwVQRHIc8",
                ThumbnailUrl = "https://img.youtube.com/vi/sVcwVQRHIc8/hqdefault.jpg",
                Source = "YouTube · Greg Kamradt",
                AuthorOrChannel = "Greg Kamradt",
                DurationOrReadTime = "52 min",
                RankingScore = 98,
                WhyThisResource = "Recommended because standard vector search hallucinates 30% of the time; hybrid search with re-ranking eliminates enterprise retrieval failures.",
                WhatYouWillLearn = "Reciprocal Rank Fusion (RRF), cross-encoder re-ranking, and bracketed citation generation.",
                StepByStepBreakdown =
                [
                    new() { StepNumber = 1, Title = "Hybrid Search Pipeline", Instruction = "Combine BM25 sparse lexical search with dense vector embeddings via RRF." },
                    new() { StepNumber = 2, Title = "Cross-Encoder Re-ranking", Instruction = "Re-rank top 25 candidate passages into top 5 definitive chunks." },
                    new() { StepNumber = 3, Title = "Citation Hygiene", Instruction = "Inject bracketed source identifiers into prompts to enforce verification." }
                ],
                PracticeExerciseTitle = "Practice: Build an RRF Hybrid Pipeline",
                PracticeExercise = "Build a hybrid retrieval pipeline in Python. Ingest a 50-page company policy PDF and verify every answer outputs paragraph citations.",
                ConnectedNodeIds = ["skill-rag"],
                WhyItMatters = "Guarantees accurate answers from internal documents without hallucination.",
                YourEdge = "You can build mission-critical document assistants for legal, banking, and medical domains.",
                NextMove = "1. Watch the video.\n2. Ingest a sample PDF.\n3. Verify citations."
            }
        };

        return new LearnHubPayload
        {
            PrimarySkill = "AI & Machine Learning",
            Goal = "Become an AI Engineer",
            CurrentStage = currentStage,
            SubInterests = subInterests,
            ActiveSubInterest = activeSub,
            NextAction = nextAction,
            GraphNodes = nodes,
            GraphEdges = edges,
            Resources = resources,
            TotalMasteredNodes = nodes.Count(n => n.Status == "Mastered"),
            TotalLearningNodes = nodes.Count(n => n.Status == "Learning"),
            TotalWeakNodes = nodes.Count(n => n.Status == "WeakArea")
        };
    }
}
