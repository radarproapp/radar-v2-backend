namespace RadarV2.Tests.UseCases;

/// <summary>
/// The careers Radar is actually for, with enough of each to ask real questions about it.
/// </summary>
/// <remarks>
/// Weighted to what people in Nigeria study and work in, because that is who this is being sold
/// to. The list is deliberately not technology-heavy: the bug that started this - an
/// International Relations student shown three senior AI engineering roles at ninety per cent -
/// is invisible if every test subject is a software engineer.
///
/// <para>Each career carries four things, and all four are needed to ask anything useful:</para>
/// <list type="bullet">
/// <item><b>Interest</b> - what the learner would type into box 1, in their own words.</item>
/// <item><b>OnTopic</b> - a headline or advert that genuinely belongs to them. If Radar cannot
/// match this, the career is invisible to the person who chose it.</item>
/// <item><b>OffTopic</b> - a headline from a different field that a sloppy matcher would hand
/// them anyway. Usually software, finance or a word their field shares with another.</item>
/// <item><b>Confusable</b> - the trap. A string that contains their subject's letters but is
/// about something else: "mass communication" against Massachusetts, "art" against "start
/// date", "banking" against bankruptcy. Every one of these has a real precedent in this
/// codebase.</item>
/// </list>
/// </remarks>
public sealed record Career(
    string Name,
    string Interest,
    string OnTopic,
    string OffTopic,
    string Confusable);

public static class CareerCorpus
{
    /// <summary>
    /// Fifty careers. Each one becomes ten use cases - see <see cref="UseCaseLibrary"/>.
    /// </summary>
    public static readonly IReadOnlyList<Career> All =
    [
        // ── health ───────────────────────────────────────────────────────────
        new("Nursing", "Nursing",
            "Graduate Nurse Residency Programme, paediatrics, Lagos University Teaching Hospital",
            "Senior .NET full-stack developer, remote",
            "Nursery school expands its intake in Ikeja"),

        new("Medicine", "Medicine",
            "Residency places open in internal medicine at UCH Ibadan",
            "Growth marketing lead at a fintech",
            "Medicinal plant trade regulated under new agriculture rules"),

        new("Pharmacy", "Pharmacy",
            "Pharmacist intern scheme, community pharmacy chain, Abuja",
            "Backend engineer, Kubernetes, remote",
            "Pharmaceutical shares rally on the Nigerian Exchange"),

        new("Public Health", "Public Health",
            "Public health officer, immunisation programme, Kano state",
            "Senior Android developer, Lagos",
            "Public holiday declared for the health sector strike"),

        new("Physiotherapy", "Physiotherapy",
            "Physiotherapist, orthopaedic rehabilitation unit, Enugu",
            "Data engineer, Snowflake, hybrid",
            "Physical infrastructure spending rises in the budget"),

        new("Nutrition and Dietetics", "Nutrition",
            "Dietitian for a maternal nutrition programme in Sokoto",
            "Cloud architect, AWS, remote",
            "Nutrient runoff from fertiliser blamed for algal bloom"),

        new("Veterinary Medicine", "Veterinary Medicine",
            "Veterinary officer, livestock health, Federal Ministry of Agriculture",
            "Frontend developer, React, remote",
            "Veteran affairs commission announces pension review"),

        new("Microbiology", "Microbiology",
            "Research assistant, antimicrobial resistance surveillance, NIMR",
            "Product manager, payments, Lagos",
            "Micro-lending operators face new capital requirements"),

        new("Biochemistry", "Biochemistry",
            "Laboratory scientist, clinical biochemistry, teaching hospital",
            "DevOps engineer, Terraform, remote",
            "Biofuel refinery breaks ground in Delta state"),

        new("Psychology", "Psychology",
            "Clinical psychologist, adolescent mental health service, Lagos",
            "Senior iOS engineer, fintech",
            "Psychometric testing firm wins procurement contract"),

        // ── law, policy and the public sector ────────────────────────────────
        new("Law", "Law",
            "Associate, corporate and commercial practice, Lagos law firm",
            "Machine learning engineer, computer vision",
            "Law Society of England dinner raises funds for charity"),

        new("International Relations", "International Relations",
            "Policy analyst, West Africa desk, international relations unit, Abuja",
            "Senior AI engineer, large language models, remote",
            "International Airport terminal relations with ground handlers"),

        new("Political Science", "Political Science",
            "Research officer, electoral governance programme, civil society organisation",
            "Full-stack engineer, Node and React",
            "Political party headquarters relocates to Abuja"),

        new("Public Administration", "Public Administration",
            "Administrative officer, federal civil service, Abuja",
            "Site reliability engineer, Google Cloud",
            "Administrative buildings to be sold under asset review"),

        new("Sociology", "Sociology",
            "Field researcher, urban livelihoods study, Lagos",
            "Blockchain developer, Solidity",
            "Social media regulation bill returns to the National Assembly"),

        new("Social Work", "Social Work",
            "Case worker, child protection unit, state ministry",
            "Platform engineer, Kafka, remote",
            "Social club memberships fall as costs rise"),

        new("Taxation", "Taxation",
            "Tax associate, transfer pricing, Big Four firm, Lagos",
            "Game developer, Unity",
            "Taxi operators protest new levy in Onitsha"),

        new("Criminology", "Criminology",
            "Research assistant, community policing evaluation, Abuja",
            "Mobile developer, Flutter",
            "Crime novel wins the Nigeria Prize for Literature"),

        new("Library and Information Science", "Library Science",
            "Librarian, digital collections, federal university",
            "Database administrator, PostgreSQL",
            "Library of code dependencies audited for vulnerabilities"),

        new("Urban and Regional Planning", "Urban Planning",
            "Town planner, Lagos State physical planning authority",
            "QA automation engineer, Cypress",
            "Urban Outfitters opens a flagship store"),

        // ── money ────────────────────────────────────────────────────────────
        new("Accounting", "Accounting",
            "Audit associate, ICAN trainee scheme, Lagos",
            "Computer vision researcher, PhD preferred",
            "Accountability report criticises ministry spending"),

        new("Banking and Finance", "Banking",
            "Retail banking graduate trainee programme, Lagos",
            "Senior Rust engineer, systems programming",
            "Insolvency and bankruptcy paralegal, two-year contract"),

        new("Economics", "Economics",
            "Research economist, macroeconomic policy unit, central bank",
            "Embedded systems engineer, C and RTOS",
            "Economy class fares rise on the Lagos-Abuja route"),

        new("Insurance", "Insurance",
            "Underwriting trainee, general insurance, Lagos",
            "Technical writer, developer documentation",
            "Insurgency displaces farmers in the north east"),

        new("Actuarial Science", "Actuarial Science",
            "Actuarial analyst, life and pensions, Lagos",
            "Growth hacker, B2B SaaS",
            "Actual spending exceeded budget, audit finds"),

        new("Investment and Capital Markets", "Investment Management",
            "Investment analyst, equities research, Lagos asset manager",
            "Linux kernel engineer, device drivers",
            "Investment in road infrastructure announced by the ministry"),

        new("Procurement and Supply Chain", "Supply Chain",
            "Supply chain analyst, FMCG distribution, Lagos",
            "Compiler engineer, LLVM",
            "Supplying water to flood-hit communities begins"),

        new("Human Resources", "Human Resources",
            "HR business partner, manufacturing group, Ogun state",
            "Security researcher, binary exploitation",
            "Human rights commission opens inquiry"),

        new("Marketing", "Marketing",
            "Brand marketing executive, consumer goods, Lagos",
            "Database internals engineer, storage layer",
            "Market women protest levy increases in Aba"),

        new("Digital Marketing", "Digital Marketing",
            "Performance marketing specialist, paid social, e-commerce",
            "Firmware engineer, automotive",
            "Digital television switchover delayed again"),

        // ── engineering and the built world ──────────────────────────────────
        new("Civil Engineering", "Civil Engineering",
            "Site engineer, highway construction, Federal Ministry of Works",
            "Senior React Native developer, remote",
            "Civil society coalition calls for electoral reform"),

        new("Mechanical Engineering", "Mechanical Engineering",
            "Maintenance engineer, cement plant, Ogun state",
            "Senior data scientist, recommendation systems",
            "Mechanism for subsidy payment to be reviewed"),

        new("Electrical Engineering", "Electrical Engineering",
            "Power distribution engineer, electricity distribution company",
            "Technical product manager, developer tools",
            "Electoral commission tests new result transmission"),

        new("Chemical Engineering", "Chemical Engineering",
            "Process engineer, fertiliser plant, Port Harcourt",
            "Front-end engineer, design systems",
            "Chemical-free farming pilot launched in Benue"),

        new("Petroleum Engineering", "Petroleum Engineering",
            "Graduate petroleum engineer, upstream operations, Port Harcourt",
            "Machine learning platform engineer, MLOps",
            "Petroleum tanker drivers suspend strike"),

        new("Geology", "Geology",
            "Exploration geologist, solid minerals survey, Jos",
            "Senior backend engineer, Go microservices",
            "Geological Survey building put up for sale"),

        new("Surveying and Geoinformatics", "Surveying",
            "Land surveyor, cadastral mapping unit, state government",
            "Staff engineer, distributed systems",
            "Surveying public opinion ahead of the election"),

        new("Architecture", "Architecture",
            "Architectural assistant, residential projects, Lagos studio",
            "Senior Python engineer, data pipelines",
            "Architecture of the banking system under review"),

        new("Quantity Surveying", "Quantity Surveying",
            "Quantity surveyor, infrastructure projects, Abuja",
            "Engineering manager, platform team",
            "Quantity of rice imported falls sharply"),

        new("Estate Management", "Estate Management",
            "Property manager, commercial real estate, Victoria Island",
            "Principal engineer, search infrastructure",
            "Estate of the late businessman settled in court"),

        // ── technology, which is real but is not everybody ───────────────────
        new("Software Engineering", "Software Engineering",
            "Graduate software engineer, backend, Lagos fintech",
            "Clinical nurse specialist, intensive care",
            "Software of the mind: a philosophy lecture series"),

        new("Data Analysis", "Data Analysis",
            "Data analyst, business intelligence, telecoms, Lagos",
            "Civil engineering site supervisor, bridge works",
            "Data of birth errors delay pension payments"),

        new("Cybersecurity", "Cybersecurity",
            "SOC analyst, security operations centre, bank",
            "Veterinary officer, livestock health",
            "Security guards at the ministry get new uniforms"),

        new("UI and UX Design", "UX Design",
            "Product designer, mobile banking app, Lagos",
            "Petroleum process engineer, refinery",
            "Designs for the new national stadium unveiled"),

        new("Product Management", "Product Management",
            "Associate product manager, payments, Lagos",
            "Physiotherapist, orthopaedic unit",
            "Product recall ordered for contaminated noodles"),

        // ── the rest of the economy ──────────────────────────────────────────
        new("Agriculture", "Agriculture",
            "Agronomist, smallholder extension programme, Kaduna",
            "Senior iOS engineer, fintech",
            "Agricultural Bank shares suspended from trading"),

        new("Education", "Teaching",
            "Secondary school mathematics teacher, Lagos state",
            "Cloud security engineer, Azure",
            "Educational Trust Fund disburses to universities"),

        new("Mass Communication", "Mass Communication",
            "Broadcast journalist, current affairs desk, Lagos",
            "Kubernetes platform engineer",
            "Massachusetts Institute of Technology summer school"),

        new("Hospitality and Tourism", "Hospitality Management",
            "Front office manager, hotel group, Abuja",
            "Senior Scala engineer, streaming data",
            "Hospital management board announces new chairman"),

        new("Fashion and Creative Enterprise", "Fashion Design",
            "Studio assistant, ready-to-wear label, Lagos",
            "Senior SRE, observability",
            "Fashionable theories of economics questioned by panel"),
    ];
}
