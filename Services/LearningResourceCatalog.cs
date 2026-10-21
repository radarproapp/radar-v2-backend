using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Curated catalog of high-utility learning resources structured in a 4-stage progression:
/// Beginner → Intermediate → Advanced → Real-world Application.
///
/// Core principles enforced:
/// 1. Ordered sequence ("Start with this" -> "Next" -> "Practice" -> "Capstone").
/// 2. Clear "Why am I learning this?" connection to user goals.
/// 3. ~80% How-to / Tutorial content (step-by-step breakdown + practice exercise).
/// 4. ~20% Current industry knowledge / new developments (4-part intelligence format).
/// 5. AI Mentor testing & review hooks.
/// </summary>
public static class LearningResourceCatalog
{
    public static List<LearningResource> GetResourcesForSkill(string interestOrGoal, string? level = null)
    {
        var normalized = $"{interestOrGoal}".ToLowerInvariant();

        List<LearningResource> pool;
        // Checked first: a cybersecurity goal contains none of the keywords below, so it used to
        // fall through to the software-engineering pool while the roadmap fell through to data
        // analytics. That mismatch is what produced "how do Excel fundamentals relate to
        // cybersecurity?".
        if (DomainVocabulary.Matches(normalized, "cyber"))
            pool = CybersecurityResourceCatalog.All();
        else if (DomainVocabulary.Matches(normalized, "data"))
            pool = DataAnalyticsResources();
        else if (normalized.Contains("ai") || normalized.Contains("machine learning") || normalized.Contains("artificial") || normalized.Contains("llm"))
            pool = AiAndMachineLearningResources();
        else if (normalized.Contains("product") || normalized.Contains("prd") || normalized.Contains("pm") || normalized.Contains("ux"))
            pool = ProductManagementResources();
        else if (normalized.Contains("finance") || normalized.Contains("fintech") || normalized.Contains("valuation") || normalized.Contains("banking") || normalized.Contains("invest"))
            pool = FinanceAndFintechResources();
        else if (normalized.Contains("entrepreneur") || normalized.Contains("startup") || normalized.Contains("venture") || normalized.Contains("business"))
            pool = EntrepreneurshipResources();
        else if (normalized.Contains("policy") || normalized.Contains("govern") || normalized.Contains("regulation"))
            pool = TechnologyPolicyResources();
        else
            // No domain matched. Software engineering is NOT a safe default — handing a nurse or a
            // policy analyst a Python course is the same failure that gave cybersecurity learners
            // Excel. Return nothing so callers fall back to the learner's own roadmap and the Learn
            // Hub's live search, instead of confidently serving the wrong curriculum.
            pool = [];

        if (string.IsNullOrWhiteSpace(level)) return pool;

        var filtered = pool.Where(r => r.Level.Equals(level, StringComparison.OrdinalIgnoreCase)).ToList();
        return filtered.Count > 0 ? filtered : pool;
    }

    public static List<LearningResource> GetResourcesForStage(int stageIndex, string interestOrGoal)
    {
        var level = stageIndex switch
        {
            0 => "Beginner",
            1 => "Intermediate",
            2 => "Advanced",
            3 => "Application",
            _ => "Application"
        };
        return GetResourcesForSkill(interestOrGoal, level);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. DATA ANALYTICS (Beginner → Intermediate → Advanced → Application)
    // ─────────────────────────────────────────────────────────────────────────

    private static List<LearningResource> DataAnalyticsResources() =>
    [
        // ── BEGINNER (Excel fundamentals, Data cleaning, Basic statistics, Charts) ──
        new()
        {
            Id = "da-beg-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "45 minutes",
            Title = "Excel Fundamentals for Data Analytics – Core Formulas & Workflows",
            Url = "https://www.youtube.com/watch?v=oxDAv_D_u8c",
            ThumbnailUrl = "https://img.youtube.com/vi/oxDAv_D_u8c/hqdefault.jpg",
            Source = "YouTube · Kevin Stratvert",
            ChannelOrAuthor = "Kevin Stratvert",
            Duration = "45 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Covers the fundamental functions, spreadsheet architecture, and cell referencing you need before moving into any data manipulation or advanced analysis.",
            WhatYouWillBuild = "A clean financial summary spreadsheet calculating automated sums, averages, conditional counts, and VLOOKUP/XLOOKUP references.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Spreadsheet Navigation & Cell Referencing", Instruction = "Master relative vs absolute cell referencing ($A$1 vs A1) to write scalable formulas across multi-column tables." },
                new() { StepNumber = 2, Title = "Essential Aggregation Functions", Instruction = "Apply SUM, AVERAGE, COUNT, COUNTA, and COUNTIF to compute key performance metrics from raw transaction lines." },
                new() { StepNumber = 3, Title = "Modern Lookup Techniques", Instruction = "Implement XLOOKUP with fallback handling to join product pricing tables to transaction logs without error cascades." },
                new() { StepNumber = 4, Title = "Conditional Formatting & Alerting", Instruction = "Configure rule-based cell highlights to flag underperforming sales territories and overdue payment balances." }
            ],
            PracticeExerciseTitle = "Practice: Clean & Aggregate Monthly Sales",
            PracticeExercise = "Download the practice sales sheet. Use XLOOKUP to match product categories, compute total revenue with SUMIFS, and highlight top 10% customers using conditional formatting.",
            QuizTopic = "Excel Core Formulas & Referencing",
            WhyItMatters = "Excel remains the most widely deployed business tool worldwide; every analyst must manipulate spreadsheets before touching complex code.",
            YourEdge = "You avoid the manual copying mistakes that plague junior staff by building robust, dynamically updating formula sheets.",
            NextMove = "1. Download the practice sheet.\n2. Complete the XLOOKUP and SUMIFS formulas.\n3. Take the 5-minute checkpoint quiz."
        },
        new()
        {
            Id = "da-beg-02",
            Order = 2,
            SequenceRole = "Next",
            EstimatedTime = "35 minutes",
            Title = "Data Cleaning & Wrangling Fundamentals in Excel & Power Query",
            Url = "https://www.youtube.com/watch?v=0rstZ3w0d8s",
            ThumbnailUrl = "https://img.youtube.com/vi/0rstZ3w0d8s/hqdefault.jpg",
            Source = "YouTube · Leila Gharani",
            ChannelOrAuthor = "Leila Gharani",
            Duration = "35 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Over 80% of real-world analytics involves diagnosing and cleaning messy, inconsistent data. Without clean data, all subsequent models and charts lie.",
            WhatYouWillBuild = "An automated Power Query cleaning pipeline that removes whitespace, standardizes phone numbers, dedupes records, and handles null values.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Diagnosing Dirty Data", Instruction = "Scan for trailing spaces, inconsistent capitalization, incorrect date parsing, and corrupt numeric characters." },
                new() { StepNumber = 2, Title = "Deduplication & Null Imputation", Instruction = "Remove duplicate customer rows and apply logical fill-down rules or explicit NA replacements." },
                new() { StepNumber = 3, Title = "Text Column Transformations", Instruction = "Split combined names with delimiter rules, standardize country codes, and trim non-printing characters." },
                new() { StepNumber = 4, Title = "Automating with Power Query", Instruction = "Record the cleaning steps in Power Query so every future monthly data dump is cleaned with a single refresh click." }
            ],
            PracticeExerciseTitle = "Practice: Clean a Corrupted Customer Dataset",
            PracticeExercise = "Clean the sample African telecom registration dataset: fix corrupted date strings, deduplicate mobile numbers, and standardize city names into uniform Title Case.",
            QuizTopic = "Data Cleaning Techniques & Power Query",
            WhyItMatters = "Executives make million-dollar decisions on analytics; feeding dirty data destroys organizational trust immediately.",
            YourEdge = "By using Power Query instead of manual edits, your cleaning steps are 100% reproducible and audit-proof.",
            NextMove = "1. Clean the provided dataset.\n2. Test that refreshing the file processes new rows correctly.\n3. Submit for AI Mentor review."
        },
        new()
        {
            Id = "da-beg-03",
            Order = 3,
            SequenceRole = "Next",
            EstimatedTime = "40 minutes",
            Title = "Basic Statistics, Charts & Business Visualisation",
            Url = "https://www.youtube.com/watch?v=xxpc-HPKN28",
            ThumbnailUrl = "https://img.youtube.com/vi/xxpc-HPKN28/hqdefault.jpg",
            Source = "YouTube · StatQuest with Josh Starmer",
            ChannelOrAuthor = "StatQuest",
            Duration = "40 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Enables you to communicate trends, distributions, variance, and outliers visually to decision-makers who cannot interpret raw numbers.",
            WhatYouWillBuild = "A clean visual reporting page with distribution histograms, trend lines, and an executive KPI summary card.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Central Tendency & Spread", Instruction = "Calculate mean, median, standard deviation, and interquartile ranges to understand the underlying spread." },
                new() { StepNumber = 2, Title = "Selecting the Right Chart Type", Instruction = "Match data structures to appropriate chart types: bar charts for categories, line charts for time-series, scatter plots for correlation." },
                new() { StepNumber = 3, Title = "Decluttering Visuals", Instruction = "Remove chart junk: eliminate unnecessary 3D effects, minimize gridlines, and highlight key data points with intentional accent colors." },
                new() { StepNumber = 4, Title = "Formulating Business Takeaways", Instruction = "Add clear descriptive headers and callouts that answer 'So what?' rather than merely stating axis labels." }
            ],
            PracticeExerciseTitle = "Practice: Build an Executive KPI Visual",
            PracticeExercise = "Take quarterly retail performance data and produce: 1) a monthly revenue trend line, 2) category comparison bar chart, and 3) a 1-sentence executive summary callout.",
            QuizTopic = "Descriptive Statistics & Visual Charting",
            WhyItMatters = "A great insight that is poorly presented will be ignored by leadership; visual storytelling drives executive action.",
            YourEdge = "You present clear, decluttered charts that focus on the business decision rather than overwhelming stakeholders with confusing graphics.",
            NextMove = "1. Create the 3 charts in Excel or Google Sheets.\n2. Verify the 1-sentence insight is actionable.\n3. Complete the Beginner stage milestone."
        },
        new()
        {
            Id = "da-beg-04",
            Order = 4,
            SequenceRole = "⚡ New Development",
            EstimatedTime = "25 minutes",
            Title = "Microsoft Copilot & Python in Excel – Modern AI Analytics Workflows",
            Url = "https://www.youtube.com/watch?v=N4P02Z9f8tA",
            ThumbnailUrl = "https://img.youtube.com/vi/N4P02Z9f8tA/hqdefault.jpg",
            Source = "Microsoft Technical Insights",
            ChannelOrAuthor = "Microsoft Tech",
            Duration = "25 min",
            Level = "Beginner",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhyAmILearningThis = "AI copilot tools and native Python inside spreadsheets are revolutionizing daily spreadsheet analytics—cutting formula writing time in half.",
            WhatHappened = "Microsoft has embedded native Python execution directly into Excel grids alongside Copilot natural language queries, allowing advanced data wrangling without switching tools.",
            WhyItMatters = "You can run complex statistical plots and sentiment analysis directly inside workbooks that non-technical managers already use.",
            YourEdge = "You combine spreadsheet familiarity with AI prompting speed, producing deep analysis in 20 minutes that used to require dedicated Python scripts.",
            NextMove = "1. Enable Python in Excel preview.\n2. Type =PY to run a Seaborn pairplot on your practice sheet.\n3. Prompt Copilot to generate trend hypotheses."
        },

        // ── INTERMEDIATE (SQL, Advanced Excel, Power BI, Data modelling) ──
        new()
        {
            Id = "da-int-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "55 minutes",
            Title = "Complete SQL for Data Analytics – Zero to Hero Query Mastery",
            Url = "https://www.youtube.com/watch?v=HXV3zeRR3h4",
            ThumbnailUrl = "https://img.youtube.com/vi/HXV3zeRR3h4/hqdefault.jpg",
            Source = "YouTube · Luke Barousse",
            ChannelOrAuthor = "Luke Barousse",
            Duration = "55 min",
            Level = "Intermediate",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "SQL is important for your goal because data analysts frequently use it to retrieve, combine and analyse data from databases. You need this skill before moving into more advanced analytics and many entry-level analyst roles.",
            WhatYouWillBuild = "A multi-table analytical query suite calculating customer cohort retention, monthly recurring revenue, and churn rates in PostgreSQL.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Database Schema & Ingestion", Instruction = "Connect to PostgreSQL and inspect relational schemas across orders, users, and product catalog tables." },
                new() { StepNumber = 2, Title = "Aggregations & Groupings", Instruction = "Write multi-level GROUP BY, HAVING, and date-truncation queries to summarize monthly revenue metrics." },
                new() { StepNumber = 3, Title = "Multi-Table Relational Joins", Instruction = "Join users, orders, and products tables with INNER and LEFT joins while avoiding duplicate row traps." },
                new() { StepNumber = 4, Title = "Common Table Expressions (CTEs)", Instruction = "Structure clean, readable analytical queries using WITH clauses instead of tangled subqueries." },
                new() { StepNumber = 5, Title = "Window Functions for Ranking", Instruction = "Calculate running totals, month-over-month growth, and customer rank using OVER (PARTITION BY ... ORDER BY)." }
            ],
            PracticeExerciseTitle = "Practice: Calculate Monthly Customer Churn in SQL",
            PracticeExercise = "Write a query with a CTE that identifies users who purchased in month T but had 0 purchases in month T+1. Compute the monthly churn percentage.",
            QuizTopic = "SQL Joins, CTEs & Window Functions",
            WhyItMatters = "Databases contain the source of truth for all enterprise operations; SQL is the primary technical test in 90% of data analyst job interviews.",
            YourEdge = "You write optimized, CTE-based queries that execute fast on production data and impress interviewers with clean formatting.",
            NextMove = "1. Set up a local PostgreSQL or SQLite database.\n2. Write the churn query.\n3. Ask AI Mentor to review your SQL optimization."
        },
        new()
        {
            Id = "da-int-02",
            Order = 2,
            SequenceRole = "Next",
            EstimatedTime = "45 minutes",
            Title = "Advanced Excel & Data Modelling – Star Schemas & DAX Basics",
            Url = "https://www.youtube.com/watch?v=m13o5g5-g9c",
            ThumbnailUrl = "https://img.youtube.com/vi/m13o5g5-g9c/hqdefault.jpg",
            Source = "YouTube · MyOnlineTrainingHub",
            ChannelOrAuthor = "MyOnlineTrainingHub",
            Duration = "45 min",
            Level = "Intermediate",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Advanced data modelling connects disparate tables into structured star schemas, allowing you to analyze millions of rows without crashing Excel.",
            WhatYouWillBuild = "A relational Power Pivot model linking fact tables to dimension tables with custom DAX calculated measures.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Fact Tables vs Dimension Tables", Instruction = "Differentiate transaction fact tables (orders, clicks) from lookup dimension tables (customers, dates, stores)." },
                new() { StepNumber = 2, Title = "Creating One-to-Many Relationships", Instruction = "Build 1-to-many relationship links in the Diagram View to ensure unambiguous filter flow." },
                new() { StepNumber = 3, Title = "Calendar Dimension Creation", Instruction = "Generate a contiguous date table to enable time intelligence comparisons (YoY, YTD, MoM)." },
                new() { StepNumber = 4, Title = "Writing Core DAX Measures", Instruction = "Create explicit measures using CALCULATE, FILTER, and DIVIDE with safe divide-by-zero protection." }
            ],
            PracticeExerciseTitle = "Practice: Build a Star Schema Model",
            PracticeExercise = "Connect 3 dimension tables (Customer, Product, Region) to a Fact_Sales table. Write a DAX measure calculating Year-to-Date (YTD) Revenue.",
            QuizTopic = "Relational Modelling & Star Schemas",
            WhyItMatters = "Flat spreadsheets break at scale; relational data modelling is the foundation of professional BI engineering.",
            YourEdge = "You build models that calculate instant answers across millions of records without lagging.",
            NextMove = "1. Build the relationships in Power Pivot.\n2. Verify the filter direction is single-direction.\n3. Take the checkpoint test."
        },
        new()
        {
            Id = "da-int-03",
            Order = 3,
            SequenceRole = "Next",
            EstimatedTime = "50 minutes",
            Title = "Build an Interactive Power BI Dashboard – End-to-End Walkthrough",
            Url = "https://www.youtube.com/watch?v=TmhQCQr_nbQ",
            ThumbnailUrl = "https://img.youtube.com/vi/TmhQCQr_nbQ/hqdefault.jpg",
            Source = "YouTube · Avi Singh",
            ChannelOrAuthor = "Avi Singh",
            Duration = "50 min",
            Level = "Intermediate",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Power BI is the market-leading enterprise BI platform. Learning to turn raw multi-table datasets into interactive dashboards makes you immediately hireable.",
            WhatYouWillBuild = "A live, interactive executive sales dashboard from a raw CSV dataset with slicers, KPI cards, and cross-filtering visuals.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Import the Raw Data", Instruction = "Open Power BI Desktop. Select 'Get Data', choose CSV/Excel, and load the raw transactional dataset." },
                new() { StepNumber = 2, Title = "Clean & Transform the Data", Instruction = "In Power Query: remove duplicate rows, verify column data types, split delimiter strings, and rename columns to clear business terms." },
                new() { StepNumber = 3, Title = "Create the Relational Model", Instruction = "Open Model view. Connect the relevant customer and product tables to the sales fact table with 1-to-many relationships." },
                new() { StepNumber = 4, Title = "Build the Visuals", Instruction = "Create required visuals: monthly revenue line chart, regional performance bar chart, and product margin treemap." },
                new() { StepNumber = 5, Title = "Assemble the Dashboard Layout", Instruction = "Arrange visuals cleanly on the canvas with executive KPI summary cards along the top and category slicers in the left sidebar." },
                new() { StepNumber = 6, Title = "Test Cross-Filtering & Calculations", Instruction = "Click through slicers to ensure all filters, drill-downs, and DAX calculations update dynamically without blank cards." }
            ],
            PracticeExerciseTitle = "Practice: Recreate the Power BI Dashboard",
            PracticeExercise = "Recreate the dashboard using the practice African retail dataset. Add a dynamic slicer for Country and test that margins calculate accurately.",
            QuizTopic = "Power BI Dashboard Architecture & Visual Design",
            WhyItMatters = "Executives and department heads rarely inspect SQL code; they interact with Power BI dashboards to drive strategic direction.",
            YourEdge = "You know how to design responsive dashboards that answer business questions in 5 seconds rather than confusing users with visual clutter.",
            NextMove = "1. Recreate the dashboard with the practice dataset.\n2. Save the .pbix file.\n3. Request an AI Mentor project review."
        },
        new()
        {
            Id = "da-int-04",
            Order = 4,
            SequenceRole = "⚡ New Development",
            EstimatedTime = "25 minutes",
            Title = "Microsoft Fabric & Direct Lake Mode: High-Velocity Enterprise BI",
            Url = "https://www.youtube.com/watch?v=U0k4cKxQzR4",
            ThumbnailUrl = "https://img.youtube.com/vi/U0k4cKxQzR4/hqdefault.jpg",
            Source = "Guy in a Cube",
            ChannelOrAuthor = "Guy in a Cube",
            Duration = "25 min",
            Level = "Intermediate",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhyAmILearningThis = "Microsoft Fabric is the biggest architectural shift in Power BI history. Understanding Direct Lake mode positions you ahead of legacy analysts.",
            WhatHappened = "Microsoft Fabric unified data warehousing, lakehouses, and Power BI into one platform, introducing Direct Lake mode which queries Delta Parquet files with zero import lag.",
            WhyItMatters = "You no longer need to schedule slow overnight data imports or worry about model file size limits when serving billions of records.",
            YourEdge = "You can design modern data solutions that stream real-time data directly to dashboards without expensive custom engineering.",
            NextMove = "1. Create a free Microsoft Fabric trial capacity.\n2. Load a Delta Lake table in OneLake.\n3. Connect a Power BI report using Direct Lake mode."
        },

        // ── ADVANCED (Python, Advanced statistics, Predictive analytics, Advanced BI) ──
        new()
        {
            Id = "da-adv-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "55 minutes",
            Title = "Python Pandas & NumPy for Advanced Analytics & Automation",
            Url = "https://www.youtube.com/watch?v=vmEHCJofslg",
            ThumbnailUrl = "https://img.youtube.com/vi/vmEHCJofslg/hqdefault.jpg",
            Source = "YouTube · Keith Galli",
            ChannelOrAuthor = "Keith Galli",
            Duration = "55 min",
            Level = "Advanced",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Python unlocks massive data scale, complex statistical modeling, and full automation that spreadsheet tools cannot handle.",
            WhatYouWillBuild = "An automated analytical pipeline that reads 500k rows, performs vectorized feature transformations, and exports clean analytical Parquet tables.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Vectorized Operations vs Slow Python Loops", Instruction = "Apply NumPy arrays and Pandas Series vectorized arithmetic to process calculations in milliseconds." },
                new() { StepNumber = 2, Title = "Complex GroupBy & Aggregations", Instruction = "Use groupby().agg() with custom lambda functions and multi-column index management." },
                new() { StepNumber = 3, Title = "Time Series Resampling & Rolling Windows", Instruction = "Set datetime indices to compute 7-day rolling revenue averages and seasonal decomposition." },
                new() { StepNumber = 4, Title = "Handling Large Files with Chunking & Parquet", Instruction = "Process gigabyte-scale datasets in memory-efficient batches and store outputs in columnar Parquet format." }
            ],
            PracticeExerciseTitle = "Practice: Build a Python Rolling Cohort Pipeline",
            PracticeExercise = "Write a Python script that ingests a transactional log, calculates 30-day customer retention cohorts, and plots the retention decay curve with Seaborn.",
            QuizTopic = "Pandas Vectorization, Resampling & Performance",
            WhyItMatters = "Advanced analytics roles demand Python literacy; it bridges the gap between basic business reporting and machine learning engineering.",
            YourEdge = "You automate weekly reporting workflows from hours of manual clicking into a single 5-second script execution.",
            NextMove = "1. Set up a Jupyter Notebook.\n2. Write the 30-day cohort retention script.\n3. Benchmark runtime against Excel."
        },
        new()
        {
            Id = "da-adv-02",
            Order = 2,
            SequenceRole = "Next",
            EstimatedTime = "45 minutes",
            Title = "Advanced Statistics & Hypothesis Testing – A/B Testing in Practice",
            Url = "https://www.youtube.com/watch?v=0oc49DyA3hU",
            ThumbnailUrl = "https://img.youtube.com/vi/0oc49DyA3hU/hqdefault.jpg",
            Source = "YouTube · StatQuest",
            ChannelOrAuthor = "Josh Starmer",
            Duration = "45 min",
            Level = "Advanced",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Separates random noise from true business signals. Essential for measuring product features, marketing campaigns, and pricing changes accurately.",
            WhatYouWillBuild = "A complete A/B experiment evaluation script calculating p-values, confidence intervals, sample size power, and statistical significance.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Formulating Null & Alternative Hypotheses", Instruction = "Define strict testable hypotheses and establish pre-experiment significance thresholds (alpha = 0.05, power = 0.80)." },
                new() { StepNumber = 2, Title = "Sample Size & MDE Estimation", Instruction = "Calculate minimum required sample sizes based on baseline conversion rate and Minimum Detectable Effect (MDE)." },
                new() { StepNumber = 3, Title = "Running Two-Sample T-Tests & Chi-Square Tests", Instruction = "Execute parametric t-tests for revenue per user and chi-square tests for binary conversion rates using scipy.stats." },
                new() { StepNumber = 4, Title = "Interpreting P-Values & Avoiding Peeking Bias", Instruction = "Explain findings to business leaders without misrepresenting p-values; guard against early stopping traps." }
            ],
            PracticeExerciseTitle = "Practice: Evaluate a Mobile Money Checkout A/B Test",
            PracticeExercise = "Analyze 50,000 checkout records: test whether the new single-step checkout significantly improved completion rates compared to the control group.",
            QuizTopic = "A/B Testing, T-Tests & Significance",
            WhyItMatters = "Tech companies run hundreds of A/B tests monthly; rigorous statistical analysis prevents costly rollout mistakes.",
            YourEdge = "You prevent executives from celebrating false-positive improvements caused by insufficient sample size or variance noise.",
            NextMove = "1. Run the Chi-Square test on the test dataset.\n2. State the recommendation in one clear executive sentence.\n3. Take the quiz."
        },
        new()
        {
            Id = "da-adv-03",
            Order = 3,
            SequenceRole = "Next",
            EstimatedTime = "50 minutes",
            Title = "Predictive Analytics with Scikit-Learn – Customer Churn Modeling",
            Url = "https://www.youtube.com/watch?v=0B5eIE_1vpU",
            ThumbnailUrl = "https://img.youtube.com/vi/0B5eIE_1vpU/hqdefault.jpg",
            Source = "YouTube · Ken Jee",
            ChannelOrAuthor = "Ken Jee",
            Duration = "50 min",
            Level = "Advanced",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Transforms historical descriptive reporting ('what happened') into proactive predictive foresight ('which customers are about to leave and why').",
            WhatYouWillBuild = "A machine learning churn classifier evaluating customer risk scores and ranking top contributing churn factors.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Feature Engineering for Tabular Data", Instruction = "Extract recency, frequency, monetary (RFM) metrics and interaction counts from raw transaction logs." },
                new() { StepNumber = 2, Title = "Train/Test Split & Stratification", Instruction = "Partition data cleanly with StratifiedKFold to handle class imbalance between churned and active users." },
                new() { StepNumber = 3, Title = "Model Training with Scikit-Learn", Instruction = "Train baseline Logistic Regression and Gradient Boosted Trees (XGBoost/LightGBM)." },
                new() { StepNumber = 4, Title = "Model Evaluation: ROC-AUC & Confusion Matrix", Instruction = "Evaluate precision-recall tradeoffs to prioritize identifying high-value customers at churn risk." },
                new() { StepNumber = 5, Title = "Feature Importance & Business Translation", Instruction = "Extract SHAP feature importance values to explain exactly which pain points trigger customer attrition." }
            ],
            PracticeExerciseTitle = "Practice: Train a Customer Churn Predictor",
            PracticeExercise = "Train a classification model on the telecom dataset. Achieve an ROC-AUC above 0.82 and write 3 tactical recommendations for customer success managers.",
            QuizTopic = "Predictive Analytics & Model Evaluation",
            WhyItMatters = "Companies spend 5x more acquiring new customers than retaining existing ones; predictive retention directly increases enterprise profit.",
            YourEdge = "You don't just report that churn increased; you provide an early-warning list of at-risk accounts 30 days before they cancel.",
            NextMove = "1. Train the model.\n2. Generate the SHAP importance summary plot.\n3. Submit predictions for AI Mentor evaluation."
        },
        new()
        {
            Id = "da-adv-04",
            Order = 4,
            SequenceRole = "⚡ New Development",
            EstimatedTime = "30 minutes",
            Title = "Automated Analytics & Local LLM Code Interpreters (DSPy & LangGraph)",
            Url = "https://www.youtube.com/watch?v=kYv9l5z5k-Q",
            ThumbnailUrl = "https://img.youtube.com/vi/kYv9l5z5k-Q/hqdefault.jpg",
            Source = "LangChain / AI Analytics",
            ChannelOrAuthor = "Harrison Chase",
            Duration = "30 min",
            Level = "Advanced",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhyAmILearningThis = "Autonomous code-interpreter agents can now query databases and generate Seaborn charts on demand. Modern analysts orchestrate these agents.",
            WhatHappened = "Modern LLM agent architectures (like LangGraph and local code sandboxes) can safely generate, execute, and self-correct Python analytics code in real time.",
            WhyItMatters = "Instead of writing standard slice-and-dice queries manually, you build conversational analytics bots that answer executive questions instantly.",
            YourEdge = "You understand both traditional data pipelines and agentic AI tooling, making you 10x more productive than traditional data analysts.",
            NextMove = "1. Build a local Python REPL tool with LangGraph.\n2. Prompt the agent to inspect a sales CSV and output statistical anomalies.\n3. Validate the code execution sandbox."
        },

        // ── APPLICATION (Real-world projects, Portfolio, Case studies, Interview prep) ──
        new()
        {
            Id = "da-app-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "60 minutes",
            Title = "End-to-End Analytics Capstone: Telecom Churn & Revenue Optimization",
            Url = "https://www.youtube.com/watch?v=qfyynHB9bB0",
            ThumbnailUrl = "https://img.youtube.com/vi/qfyynHB9bB0/hqdefault.jpg",
            Source = "YouTube · Alex The Analyst",
            ChannelOrAuthor = "Alex The Analyst",
            Duration = "60 min",
            Level = "Application",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Employers and clients don't hire you based on video watch hours; they hire proof of real-world execution. This capstone becomes the center of your portfolio.",
            WhatYouWillBuild = "A comprehensive, production-grade portfolio project: raw SQL extraction, Python cleaning pipeline, interactive Power BI dashboard, and executive memo.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Business Problem Framing", Instruction = "Define the business problem: a telecommunications company is losing $2.4M annually to customer churn in West Africa." },
                new() { StepNumber = 2, Title = "Data Extraction & Database Design", Instruction = "Write modular SQL scripts to extract customer usage logs, network latency tickets, and payment history into clean views." },
                new() { StepNumber = 3, Title = "Exploratory Analysis & Root Cause Diagnosis", Instruction = "Use Python to test hypotheses: discover that customers with fiber internet experience 3x churn due to frequent outage tickets." },
                new() { StepNumber = 4, Title = "Interactive Executive Dashboard Construction", Instruction = "Design a 3-page Power BI dashboard: Page 1 Executive Overview, Page 2 Network Quality Analysis, Page 3 Customer Attrition Risk." },
                new() { StepNumber = 5, Title = "Writing the Executive Decision Memo", Instruction = "Structure a 1-page business memo summarizing the findings, estimated revenue salvage ($850K), and 3 tactical recommendations." }
            ],
            PracticeExerciseTitle = "Practice: Complete & Document Your Capstone",
            PracticeExercise = "Package your SQL scripts, Python notebooks, Power BI screenshots, and 1-page executive memo into a clean GitHub repository.",
            QuizTopic = "End-to-End Business Analytics & Strategic Impact",
            WhyItMatters = "Demonstrating that you can take an ambiguous business problem from raw data to an executive decision memo is the ultimate proof of job readiness.",
            YourEdge = "You present business outcomes and dollar impact, immediately outshining candidates who only show generic tutorials.",
            NextMove = "1. Complete the capstone project.\n2. Draft the executive memo.\n3. Submit your project link for AI Mentor review."
        },
        new()
        {
            Id = "da-app-02",
            Order = 2,
            SequenceRole = "Next",
            EstimatedTime = "40 minutes",
            Title = "Building a World-Class Data Analytics Portfolio (GitHub & NovyPro)",
            Url = "https://www.youtube.com/watch?v=pjyLpX54_7k",
            ThumbnailUrl = "https://img.youtube.com/vi/pjyLpX54_7k/hqdefault.jpg",
            Source = "YouTube · Chandoo",
            ChannelOrAuthor = "Chandoo",
            Duration = "40 min",
            Level = "Application",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Having great projects is useless if hiring managers cannot find or evaluate them in under 60 seconds.",
            WhatYouWillBuild = "A public data portfolio with interactive web embeds, clean GitHub README documentation, data dictionary tables, and Loom video walkthroughs.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Curating 3 High-Impact Projects", Instruction = "Select 3 distinct domain projects: 1) Financial/Sales Analytics, 2) Operational/Supply Chain, 3) Customer Churn/Predictive." },
                new() { StepNumber = 2, Title = "Structuring the GitHub README", Instruction = "Include: Project Title, Business Problem, Dataset Description, Key Insights, Interactive Demo Link, and Recommendations." },
                new() { StepNumber = 3, Title = "Publishing to NovyPro / Power BI Web", Instruction = "Embed your interactive Power BI dashboard to the web so recruiters can click and filter without downloading files." },
                new() { StepNumber = 4, Title = "Recording a 2-Minute Video Walkthrough", Instruction = "Record a Loom video walking through the business insight and your methodology with executive confidence." }
            ],
            PracticeExerciseTitle = "Practice: Publish Your Portfolio Site",
            PracticeExercise = "Create your GitHub repository with complete documentation, embed your dashboard on NovyPro, and record your 2-minute Loom walkthrough.",
            QuizTopic = "Portfolio Framing & Executive Presentation",
            WhyItMatters = "Recruiters spend an average of 30 seconds on a profile; an interactive portfolio immediately establishes credibility.",
            YourEdge = "Your portfolio looks like that of a seasoned consulting analyst rather than a student seeking an entry-level position.",
            NextMove = "1. Create your portfolio repository.\n2. Record the 2-minute Loom.\n3. Add link to your profile."
        },
        new()
        {
            Id = "da-app-03",
            Order = 3,
            SequenceRole = "Next",
            EstimatedTime = "45 minutes",
            Title = "Business Case Studies: Framing Data Insights into Financial Impact",
            Url = "https://www.youtube.com/watch?v=F3G8B87yXrk",
            ThumbnailUrl = "https://img.youtube.com/vi/F3G8B87yXrk/hqdefault.jpg",
            Source = "Harvard Business Review / McKinsey Insights",
            ChannelOrAuthor = "McKinsey Quarterly",
            Duration = "45 min",
            Level = "Application",
            Type = LearningResourceType.BlogPost,
            WhyAmILearningThis = "Senior analysts don't just calculate averages; they identify revenue opportunities, cost reductions, and operational efficiencies that change company trajectory.",
            WhatHappened = "Leading tech enterprises (Uber, Amazon, Flutterwave) require analysts to frame findings using the Pyramid Principle: Answer First, followed by supporting quantitative evidence.",
            WhyItMatters = "If you present data without connecting it to revenue or cost, leadership will nod politely and take zero action.",
            YourEdge = "You speak the language of CEOs and CFOs: ROI, customer lifetime value, cashflow impact, and margin expansion.",
            NextMove = "1. Read the McKinsey case study breakdown.\n2. Rewrite your capstone summary using the Pyramid Principle.\n3. Highlight exactly how many dollars your recommendation saves."
        },
        new()
        {
            Id = "da-app-04",
            Order = 4,
            SequenceRole = "Next",
            EstimatedTime = "50 minutes",
            Title = "Data Analyst Interview Mastery: Live SQL, Business Cases & Take-Homes",
            Url = "https://www.youtube.com/watch?v=o04_7Y23lR0",
            ThumbnailUrl = "https://img.youtube.com/vi/o04_7Y23lR0/hqdefault.jpg",
            Source = "YouTube · StrataScratch",
            ChannelOrAuthor = "Nathan Rosidi",
            Duration = "50 min",
            Level = "Application",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Prepares you to ace high-pressure technical interviews, live SQL whiteboard tests, metrics breakdown questions, and take-home assignments.",
            WhatYouWillBuild = "A comprehensive interview playbook containing answers to top 25 technical SQL challenges, behavioral STAR responses, and business metrics questions.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Acing Live SQL Coding Rounds", Instruction = "Master common whiteboard patterns: self-joins, running aggregations, consecutive active day streaks, and rank ties." },
                new() { StepNumber = 2, Title = "Answering 'Metric Drop' Case Questions", Instruction = "Structure answers when asked: 'Revenue dropped 8% last Tuesday—how do you investigate?' (Internal vs External, Funnel Segmentation, Device/Region breakdowns)." },
                new() { StepNumber = 3, Title = "Cracking the 48-Hour Take-Home Test", Instruction = "Best practices for take-home tests: clean repository structure, documented assumptions, elegant visualizations, and concise executive summary." },
                new() { StepNumber = 4, Title = "Behavioral & Stakeholder Alignment Questions", Instruction = "Structure STAR responses for handling conflicting stakeholder priorities and explaining complex numbers to non-technical partners." }
            ],
            PracticeExerciseTitle = "Practice: Solve 3 FAANG SQL Interview Challenges",
            PracticeExercise = "Complete 3 live SQL challenges: 1) Top 3 Customers by Region, 2) 7-Day Active User Streak, 3) Month-over-Month Revenue Percent Change.",
            QuizTopic = "Data Analyst Interview Technical Challenges",
            WhyItMatters = "Passing the technical interview is the final gate between your preparation and landing your dream job offer.",
            YourEdge = "You talk through your thought process clearly and ask clarifying questions before writing code—the #1 trait senior interviewers look for.",
            NextMove = "1. Solve the 3 practice SQL challenges.\n2. Rehearse the 'Metric Drop' case study aloud.\n3. Take the interview readiness quiz."
        }
    ];

    // ─────────────────────────────────────────────────────────────────────────
    // 2. AI & MACHINE LEARNING (Beginner → Intermediate → Advanced → Application)
    // ─────────────────────────────────────────────────────────────────────────

    private static List<LearningResource> AiAndMachineLearningResources() =>
    [
        // BEGINNER
        new()
        {
            Id = "ai-beg-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "48 minutes",
            Title = "Python for Machine Learning & AI – Complete Hands-On Beginner Course",
            Url = "https://www.youtube.com/watch?v=i_LwzRVP7bg",
            ThumbnailUrl = "https://img.youtube.com/vi/i_LwzRVP7bg/hqdefault.jpg",
            Source = "YouTube · freeCodeCamp",
            ChannelOrAuthor = "freeCodeCamp.org",
            Duration = "48 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Python is the lingua franca of artificial intelligence. You need clean local environment setup and scientific package mastery before training any models.",
            WhatYouWillBuild = "A clean Python machine learning development environment and a working classification script predicting real-world outcomes.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Environment Setup & Virtualenv", Instruction = "Install Python 3.11+, set up a virtual environment, and configure Jupyter Notebook with essential scientific libraries (NumPy, Pandas, Matplotlib)." },
                new() { StepNumber = 2, Title = "Data Exploration & Feature Structuring", Instruction = "Load a raw tabular dataset, inspect data types, clean missing values, and structure inputs into training matrices X and target vectors y." },
                new() { StepNumber = 3, Title = "Model Instantiation & Training", Instruction = "Fit your first Scikit-Learn baseline estimator, configure hyperparameters, and train the model on structured training batches." },
                new() { StepNumber = 4, Title = "Evaluation & Test Inference", Instruction = "Run test set predictions, generate a confusion matrix, and calculate accuracy, precision, and recall scores." }
            ],
            PracticeExerciseTitle = "Practice: Train a Customer Churn Classifier",
            PracticeExercise = "Ingest a Kaggle telecom churn dataset. Split 80/20 train/test, train a Random Forest classifier, and print the classification report.",
            QuizTopic = "Python ML Foundations & Scikit-Learn",
            WhyItMatters = "Mastering foundational data ingestion and model fitting is the non-negotiable bedrock before advancing to deep learning and LLM architectures.",
            YourEdge = "You bridge theoretical ML concepts into clean, reproducible Python code that runs locally on commodity hardware without cloud lock-in.",
            NextMove = "1. Clone the starter repository.\n2. Ingest the dataset and run training.\n3. Take the 5-minute checkpoint quiz."
        },
        new()
        {
            Id = "ai-beg-02",
            Order = 2,
            SequenceRole = "Next",
            EstimatedTime = "36 minutes",
            Title = "Building Your First Neural Network from Scratch in PyTorch",
            Url = "https://www.youtube.com/watch?v=GIsg-ZUy0MY",
            ThumbnailUrl = "https://img.youtube.com/vi/GIsg-ZUy0MY/hqdefault.jpg",
            Source = "YouTube · Aladdin Persson",
            ChannelOrAuthor = "Aladdin Persson",
            Duration = "36 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "PyTorch is the undisputed framework used by OpenAI, Meta, and top research labs to train frontier AI models.",
            WhatYouWillBuild = "A modular multi-layer perceptron (MLP) in PyTorch that classifies structured customer transactions.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "PyTorch Tensor Fundamentals", Instruction = "Understand PyTorch tensors, memory allocation, and autograd computation graphs for backpropagation." },
                new() { StepNumber = 2, Title = "Building the nn.Module Architecture", Instruction = "Define input layers, linear transformations, ReLU activation functions, and output probability dimensions." },
                new() { StepNumber = 3, Title = "Writing the Training Loop", Instruction = "Implement the canonical zero_grad -> forward -> loss -> backward -> optimizer.step() loop with CrossEntropyLoss." },
                new() { StepNumber = 4, Title = "Tracking Validation Loss & Checkpointing", Instruction = "Log validation metrics per epoch and save the best model weights to disk." }
            ],
            PracticeExerciseTitle = "Practice: Implement a 3-Layer PyTorch MLP",
            PracticeExercise = "Code a 3-layer neural network in PyTorch with Dropout layers to prevent overfitting. Train for 15 epochs and save the model checkpoint.",
            QuizTopic = "PyTorch Tensors, Autograd & Training Loops",
            WhyItMatters = "Understanding the PyTorch autograd engine unlocks the ability to customize architectures and debug training instability.",
            YourEdge = "Unlike engineers who only call black-box APIs, you understand gradient backpropagation and loss convergence at the mathematical layer.",
            NextMove = "1. Implement the PyTorch network.\n2. Verify validation loss decreases.\n3. Request AI Mentor code review."
        },
        new()
        {
            Id = "ai-beg-03",
            Order = 3,
            SequenceRole = "⚡ New Development",
            EstimatedTime = "42 minutes",
            Title = "State of AI 2026: Reasoning Models, Small Language Models & Edge Inference",
            Url = "https://www.youtube.com/watch?v=zjkBMFhNj_g",
            ThumbnailUrl = "https://img.youtube.com/vi/zjkBMFhNj_g/hqdefault.jpg",
            Source = "YouTube · Andrej Karpathy",
            ChannelOrAuthor = "Andrej Karpathy",
            Duration = "42 min",
            Level = "Beginner",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhyAmILearningThis = "AI is moving from brute-force next-token prediction to test-time reasoning models and efficient edge deployment.",
            WhatHappened = "Andrej Karpathy explores the transition from raw next-token prediction to test-time reasoning and ultra-compact small language models (SLMs) running on edge devices.",
            WhyItMatters = "Understanding the architectural shift towards test-time compute allows you to build cost-effective AI features without relying on massive million-dollar GPU clusters.",
            YourEdge = "You design lean, hybrid AI systems that run lightweight 7B/14B reasoning models locally, drastically cutting inference bills while maintaining quality.",
            NextMove = "1. Read Karpathy's breakdown on test-time compute.\n2. Benchmark DeepSeek-R1-Distill-Qwen-7B against standard models.\n3. Evaluate latency for local customer service bots."
        },

        // INTERMEDIATE
        new()
        {
            Id = "ai-int-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "52 minutes",
            Title = "Production RAG with Hybrid Search, Re-ranking & Citations",
            Url = "https://www.youtube.com/watch?v=sVcwVQRHIc8",
            ThumbnailUrl = "https://img.youtube.com/vi/sVcwVQRHIc8/hqdefault.jpg",
            Source = "YouTube · Greg Kamradt",
            ChannelOrAuthor = "Greg Kamradt",
            Duration = "52 min",
            Level = "Intermediate",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "RAG is the most in-demand enterprise AI skill. Companies need LLMs grounded on their private internal knowledge bases.",
            WhatYouWillBuild = "An enterprise RAG system combining BM25 keyword search, dense vector retrieval, cross-encoder re-ranking, and automated source citation.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Hybrid Search Implementation", Instruction = "Combine sparse BM25 lexical search with dense vector embeddings via Reciprocal Rank Fusion (RRF)." },
                new() { StepNumber = 2, Title = "Cross-Encoder Re-Ranking", Instruction = "Pass top candidate passages through a cross-encoder to produce the definitive top 5 chunks." },
                new() { StepNumber = 3, Title = "Prompt Injection with Source Markers", Instruction = "Format the prompt context with bracketed source identifiers ([Source 1: Policy Page 14]) to enforce citation hygiene." },
                new() { StepNumber = 4, Title = "Hallucination Evaluation", Instruction = "Implement Ragas metrics to score answer relevance and ground truth alignment programmatically." }
            ],
            PracticeExerciseTitle = "Practice: Build an RRF Hybrid Pipeline",
            PracticeExercise = "Build a hybrid retrieval pipeline in Python. Ingest a 50-page company policy PDF and verify every answer outputs paragraph citations.",
            QuizTopic = "Hybrid Search, RRF & Vector Retrieval",
            WhyItMatters = "Naïve vector search retrieves irrelevant context 30% of the time; hybrid search with re-ranking eliminates enterprise hallucination.",
            YourEdge = "You build mission-critical document assistants for legal, banking, and medical domains where accuracy and auditability are non-negotiable.",
            NextMove = "1. Run the RAG test.\n2. Verify citation accuracy.\n3. Take the quiz."
        },
        new()
        {
            Id = "ai-int-02",
            Order = 2,
            SequenceRole = "Next",
            EstimatedTime = "45 minutes",
            Title = "Building Autonomous AI Agents with LangGraph & Tool Calling",
            Url = "https://www.youtube.com/watch?v=1bPuqDcwV5w",
            ThumbnailUrl = "https://img.youtube.com/vi/1bPuqDcwV5w/hqdefault.jpg",
            Source = "YouTube · LangChain",
            ChannelOrAuthor = "LangChain",
            Duration = "45 min",
            Level = "Intermediate",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Autonomous agents that can invoke tools, search databases, and manage state are replacing simple single-turn chatbots.",
            WhatYouWillBuild = "A multi-step autonomous market intelligence agent with state management, conditional edges, web search, and SQLite tools.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "StateGraph Schema Definition", Instruction = "Define the TypedDict agent state holding conversation history, tool outputs, and execution decision flags." },
                new() { StepNumber = 2, Title = "Equipping External Tool Nodes", Instruction = "Define deterministic Python functions for HTTP web requests, currency conversion, and database queries." },
                new() { StepNumber = 3, Title = "Configuring Conditional Edges", Instruction = "Write router functions that inspect LLM tool call requests and branch to either tool execution or user response." },
                new() { StepNumber = 4, Title = "State Persistence & Human-in-the-Loop", Instruction = "Add MemorySaver checkpointing to allow human inspection before executing sensitive financial actions." }
            ],
            PracticeExerciseTitle = "Practice: Build a 3-Node LangGraph Agent",
            PracticeExercise = "Create a research agent that queries a database, fetches live currency exchange rates, and compiles a Markdown summary with human confirmation.",
            QuizTopic = "LangGraph State Machines & Tool Routing",
            WhyItMatters = "Cyclical state graphs are the industry standard for production agentic applications that must run multi-day automated workflows.",
            YourEdge = "You construct software agents that don't get stuck in hallucination loops and can interact reliably with external enterprise APIs.",
            NextMove = "1. Code the LangGraph agent.\n2. Test state recovery after interruption.\n3. Take checkpoint test."
        },
        new()
        {
            Id = "ai-int-03",
            Order = 3,
            SequenceRole = "⚡ New Development",
            EstimatedTime = "26 minutes",
            Title = "DeepSeek-R1 Architecture Breakdown: Pure RL & Reasoning Emergence",
            Url = "https://www.youtube.com/watch?v=3c_U2A7_Gyo",
            ThumbnailUrl = "https://img.youtube.com/vi/3c_U2A7_Gyo/hqdefault.jpg",
            Source = "YouTube · AI Explained",
            ChannelOrAuthor = "AI Explained",
            Duration = "26 min",
            Level = "Intermediate",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhyAmILearningThis = "DeepSeek proved that elite reasoning emerges through rule-based RL reward shaping at a fraction of traditional training cost.",
            WhatHappened = "Deep dive into how DeepSeek developed competitive reasoning models using pure reinforcement learning without massive supervised fine-tuning.",
            WhyItMatters = "Shows that specialized reasoning can be trained into smaller models using automated verifiable rewards (math correctness, syntax validation).",
            YourEdge = "You design domain-specific reasoning pipelines that don't depend on massive multi-million dollar human labeling budgets.",
            NextMove = "1. Read the DeepSeek-R1 paper.\n2. Inspect rule-based reward verifiers.\n3. Implement a simple code verification reward function."
        },

        // ADVANCED
        new()
        {
            Id = "ai-adv-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "118 minutes",
            Title = "Full Custom Training: Tokenizer, RoPE & Transformer Architecture from Scratch",
            Url = "https://www.youtube.com/watch?v=kCc8FmEb1nY",
            ThumbnailUrl = "https://img.youtube.com/vi/kCc8FmEb1nY/hqdefault.jpg",
            Source = "YouTube · Andrej Karpathy",
            ChannelOrAuthor = "Andrej Karpathy",
            Duration = "118 min",
            Level = "Advanced",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Understanding every matrix multiplication inside the attention mechanism is what separates elite AI engineers from surface-level API wrappers.",
            WhatYouWillBuild = "A complete GPT-style decoder-only transformer with Byte-Pair Encoding (BPE), Rotary Position Embeddings (RoPE), and Multi-Head Attention coded from scratch in pure PyTorch.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Byte-Pair Encoding Tokenizer Training", Instruction = "Train a custom BPE tokenizer on domain text and handle vocabulary merges and compression ratio optimization." },
                new() { StepNumber = 2, Title = "Causal Multi-Head Attention with RoPE", Instruction = "Implement query, key, value matrix projections, rotary positional embeddings, and masked causal attention score calculation." },
                new() { StepNumber = 3, Title = "Feed-Forward SwiGLU Network & RMSNorm", Instruction = "Construct modern SwiGLU non-linearities and pre-layer normalization with RMSNorm for training stability." },
                new() { StepNumber = 4, Title = "Autoregressive Sampling & Generation", Instruction = "Write top-p nucleus sampling and temperature scaling routines for fluent autoregressive text generation." }
            ],
            PracticeExerciseTitle = "Practice: Train a Mini Transformer on TinyStories",
            PracticeExercise = "Code the causal attention block from scratch in PyTorch. Train a 10M parameter language model on TinyStories and verify coherent generation.",
            QuizTopic = "Transformer Attention Mechanics & RoPE",
            WhyItMatters = "You can diagnose deep model failures, optimize GPU kernel performance, and build custom architectures for specialized hardware.",
            YourEdge = "You have mathematical and implementation mastery over modern transformer architectures.",
            NextMove = "1. Code the attention layer.\n2. Train for 500 steps.\n3. Submit loss curve to AI Mentor."
        },

        // APPLICATION (Real-world projects, Portfolio, Case studies, Interview prep)
        new()
        {
            Id = "ai-app-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "60 minutes",
            Title = "AI Engineering Capstone: End-to-End Enterprise Agent & Inference Cluster",
            Url = "https://www.youtube.com/watch?v=Wz7n9Xy1g8Y",
            ThumbnailUrl = "https://img.youtube.com/vi/Wz7n9Xy1g8Y/hqdefault.jpg",
            Source = "GitHub · Production AI Patterns",
            ChannelOrAuthor = "Production AI Patterns",
            Duration = "60 min",
            Level = "Application",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Hiring teams hire engineers who have built, evaluated, and deployed full end-to-end AI applications into production with monitoring.",
            WhatYouWillBuild = "A full-stack enterprise AI product: local vLLM serving, LangGraph autonomous agent with tool execution, PostgreSQL persistence, and React UI.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Containerized High-Throughput Inference", Instruction = "Deploy an open-weights model using vLLM Docker container with PagedAttention and continuous batching." },
                new() { StepNumber = 2, Title = "Asynchronous FastAPI Backend", Instruction = "Build FastAPI endpoints with Server-Sent Events (SSE) streaming tokens, request validation, and API authentication." },
                new() { StepNumber = 3, Title = "Automated Evaluation & Guardrails", Instruction = "Implement NeMo Guardrails or Llama-Guard to sanitize prompt inputs and filter hallucinations." },
                new() { StepNumber = 4, Title = "Observability & Latency Tracing", Instruction = "Add OpenTelemetry or Langfuse tracing to log latency percentiles (p50, p99), token consumption, and failure modes." }
            ],
            PracticeExerciseTitle = "Practice: Deploy & Benchmark Your AI Application",
            PracticeExercise = "Deploy your containerized service, run a 50-concurrent-user benchmark with Locust, and record an architectural walkthrough video.",
            QuizTopic = "Production AI Deployment & System Design",
            WhyItMatters = "Proves you can bridge the gap between experimental notebook prototypes and enterprise-grade software systems.",
            YourEdge = "You can confidently design, estimate cloud infrastructure costs for, and lead enterprise AI deployments.",
            NextMove = "1. Complete the capstone project.\n2. Benchmark throughput.\n3. Request AI Mentor architectural review."
        },
        new()
        {
            Id = "ai-app-02",
            Order = 2,
            SequenceRole = "Next",
            EstimatedTime = "45 minutes",
            Title = "AI System Design Interview Preparation: RAG, Search & Agentic Clusters",
            Url = "https://www.youtube.com/watch?v=04_7Y23lR0",
            ThumbnailUrl = "https://img.youtube.com/vi/04_7Y23lR0/hqdefault.jpg",
            Source = "YouTube · ByteByteGo",
            ChannelOrAuthor = "Alex Xu",
            Duration = "45 min",
            Level = "Application",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "System design rounds are the primary differentiator between Senior AI Engineers and junior developers in tech hiring.",
            WhatYouWillBuild = "An AI System Design cheat sheet covering vector database sharding, latency mitigation, caching strategies, and evaluation loops.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Designing a Large-Scale Semantic Search Engine", Instruction = "Calculate QPS, storage requirements, vector index memory footprints, and ANN search latency." },
                new() { StepNumber = 2, Title = "Handling Rate Limits & Degradation", Instruction = "Architect fallbacks: semantic caching with Redis, graceful model degradation to smaller models, and queue management." },
                new() { StepNumber = 3, Title = "Evaluating LLM Output Quality at Scale", Instruction = "Design offline automated eval pipelines using LLM-as-a-judge and human-in-the-loop audit sampling." }
            ],
            PracticeExerciseTitle = "Practice: Design a Multimodal Document Assistant",
            PracticeExercise = "Draw the architecture diagram for an enterprise assistant handling 10,000 requests/minute. Detail data ingestion, embedding cache, and security controls.",
            QuizTopic = "AI System Design & Scaling Tradeoffs",
            WhyItMatters = "A strong system design performance commands senior-level compensation and technical leadership positions.",
            YourEdge = "You make principled engineering tradeoffs grounded in latency numbers, costs, and reliability guarantees.",
            NextMove = "1. Diagram the architecture.\n2. Review with AI Mentor.\n3. Practice explaining the tradeoffs aloud."
        }
    ];

    // ─────────────────────────────────────────────────────────────────────────
    // 3. PRODUCT MANAGEMENT (Beginner → Intermediate → Advanced → Application)
    // ─────────────────────────────────────────────────────────────────────────

    private static List<LearningResource> ProductManagementResources() =>
    [
        // BEGINNER
        new()
        {
            Id = "pm-beg-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "40 minutes",
            Title = "Product Management Fundamentals: The First 90 Days",
            Url = "https://www.youtube.com/watch?v=d_kS_Z9k2f4",
            ThumbnailUrl = "https://img.youtube.com/vi/d_kS_Z9k2f4/hqdefault.jpg",
            Source = "YouTube · Lenny's Podcast",
            ChannelOrAuthor = "Lenny Rachitsky",
            Duration = "40 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Teaches the core operating rhythm of a product manager: discovering user problems, defining outcomes, and aligning teams.",
            WhatYouWillBuild = "A comprehensive Product Requirements Document (PRD) template with user problem statements, hypotheses, and success metrics.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Defining the Problem Statement", Instruction = "Write concise problem statements separating user pain points from premature feature solutions." },
                new() { StepNumber = 2, Title = "Conducting Customer Discovery", Instruction = "Structure non-leading user interview scripts using the 'Mom Test' principles." },
                new() { StepNumber = 3, Title = "Defining Success Metrics", Instruction = "Identify input metrics that directly influence the primary North Star metric." }
            ],
            PracticeExerciseTitle = "Practice: Draft a PRD for a Mobile Feature",
            PracticeExercise = "Draft a 2-page PRD for an automated savings feature on a mobile banking app. Include problem statement, target persona, and success metrics.",
            QuizTopic = "PRD Writing & Customer Discovery",
            WhyItMatters = "Strong PRDs prevent teams from wasting months building software that users don't need.",
            YourEdge = "You write crisp, actionable specs that engineers love and executives respect.",
            NextMove = "1. Draft your PRD.\n2. Take the quiz.\n3. Submit for AI Mentor review."
        },
        new()
        {
            Id = "pm-beg-02",
            Order = 2,
            SequenceRole = "⚡ New Development",
            EstimatedTime = "25 minutes",
            Title = "AI-Native Product Management: Specs, Wireframes & Rapid Prototyping",
            Url = "https://www.youtube.com/watch?v=x7k_pm09a8",
            ThumbnailUrl = "https://img.youtube.com/vi/x7k_pm09a8/hqdefault.jpg",
            Source = "YouTube · Shreyas Doshi",
            ChannelOrAuthor = "Shreyas Doshi",
            Duration = "25 min",
            Level = "Beginner",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhyAmILearningThis = "AI tools allow solo PMs to generate functional clickable prototypes and user stories in minutes rather than weeks.",
            WhatHappened = "Modern PMs use AI prototyping platforms (v0, Claude Artifacts) to test concepts directly with customers before writing Jira tickets.",
            WhyItMatters = "Shortens product feedback loops from months to days, drastically cutting development waste.",
            YourEdge = "You bring working interactive prototypes to stakeholder meetings rather than dry slide decks.",
            NextMove = "1. Prototype a feature using AI.\n2. Run a 10-minute user test.\n3. Document feedback."
        },

        // INTERMEDIATE
        new()
        {
            Id = "pm-int-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "45 minutes",
            Title = "Product Strategy & Competitive Moats in Fast-Moving Markets",
            Url = "https://www.youtube.com/watch?v=F3G8B87yXrk",
            ThumbnailUrl = "https://img.youtube.com/vi/F3G8B87yXrk/hqdefault.jpg",
            Source = "YouTube · Reforge",
            ChannelOrAuthor = "Reforge",
            Duration = "45 min",
            Level = "Intermediate",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Product managers must defend their products against copycats through network effects, switching costs, and proprietary data moats.",
            WhatYouWillBuild = "A 1-page Product Strategy Memo outlining market positioning, counter-positioning tactics, and defensible moats.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "7 Powers Framework Analysis", Instruction = "Evaluate the 7 powers (network effects, scale economies, switching costs, branding, cornered resource, process power, counter-positioning)." },
                new() { StepNumber = 2, Title = "Growth Loops vs Linear Funnels", Instruction = "Design self-reinforcing acquisition and engagement loops (e.g., viral loops, content loops, paid loops)." },
                new() { StepNumber = 3, Title = "Roadmap Prioritization Frameworks", Instruction = "Score competing initiatives using RICE (Reach, Impact, Confidence, Effort) to allocate engineering resources." }
            ],
            PracticeExerciseTitle = "Practice: Design a Growth Loop for Fintech",
            PracticeExercise = "Map out a self-reinforcing growth loop for an African peer-to-peer payments app. Calculate loop cycle time and conversion efficiency.",
            QuizTopic = "Growth Loops, Moats & RICE Scoring",
            WhyItMatters = "Features can be copied in weeks; defensible strategy and self-sustaining growth loops cannot.",
            YourEdge = "You prioritize roadmap initiatives based on compounding business value rather than who shouts loudest in meetings.",
            NextMove = "1. Map the growth loop.\n2. Complete RICE scoring.\n3. Take the quiz."
        },

        // ADVANCED
        new()
        {
            Id = "pm-adv-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "50 minutes",
            Title = "Product Analytics, Retention Curves & A/B Experimentation at Scale",
            Url = "https://www.youtube.com/watch?v=xxpc-HPKN28",
            ThumbnailUrl = "https://img.youtube.com/vi/xxpc-HPKN28/hqdefault.jpg",
            Source = "YouTube · Amplitude Analytics",
            ChannelOrAuthor = "Amplitude",
            Duration = "50 min",
            Level = "Advanced",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Data-driven PMs make decisions based on retention cohorts, funnel conversion drop-offs, and statistically significant experiments.",
            WhatYouWillBuild = "A comprehensive retention analysis framework identifying the core 'Aha!' moment that drives long-term customer stickiness.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Cohort Retention Curve Diagnostics", Instruction = "Analyze whether user retention curves flatten (product-market fit) or drop to zero (leaky bucket)." },
                new() { StepNumber = 2, Title = "Behavioral Funnel Analysis", Instruction = "Pinpoint exact funnel step drop-offs and test friction-reducing interventions." },
                new() { StepNumber = 3, Title = "Designing Rigorous A/B Experiments", Instruction = "Formulate testable hypotheses, sample sizes, and guardrail metrics to avoid revenue cannibalization." }
            ],
            PracticeExerciseTitle = "Practice: Diagnose Retention & Define the 'Aha!' Moment",
            PracticeExercise = "Analyze user event logs to find the behavior correlated with 90-day retention (e.g. 'users who send 3 payments in week 1 retain at 70%').",
            QuizTopic = "Retention Cohorts, Funnels & Experimentation",
            WhyItMatters = "Growth without retention is just pouring water into a leaky bucket; sustainable businesses are built on flat retention curves.",
            YourEdge = "You identify high-leverage product levers that measurably increase customer lifetime value.",
            NextMove = "1. Identify the 'Aha!' metric.\n2. Design the A/B test.\n3. Take the checkpoint test."
        },

        // APPLICATION
        new()
        {
            Id = "pm-app-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "60 minutes",
            Title = "Product Management Portfolio: End-to-End Case Study & Product Teardown",
            Url = "https://www.youtube.com/watch?v=F3G8B87yXrk",
            ThumbnailUrl = "https://img.youtube.com/vi/F3G8B87yXrk/hqdefault.jpg",
            Source = "YouTube · Exponent PM",
            ChannelOrAuthor = "Stephen Cognetta",
            Duration = "60 min",
            Level = "Application",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Top tech companies hire PMs based on their demonstrated ability to dissect products, formulate strategies, and present clear solutions.",
            WhatYouWillBuild = "A world-class Product Teardown case study evaluating a major market product with user feedback, wireframes, and executive launch plan.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Selecting the Target Product", Instruction = "Choose an important product in your target sector (e.g. Flutterwave, PiggyVest, WhatsApp Payments)." },
                new() { StepNumber = 2, Title = "User Pain Point Discovery", Instruction = "Gather reviews, user tweets, and conduct 3 interviews to uncover unaddressed customer friction." },
                new() { StepNumber = 3, Title = "Feature Design & Interactive Prototype", Instruction = "Design a prioritized solution with low-fidelity Figma wireframes and user flow diagrams." },
                new() { StepNumber = 4, Title = "Go-to-Market & Launch Plan", Instruction = "Define phased rollout schedule, feature flags, success metrics, and risk mitigation strategies." }
            ],
            PracticeExerciseTitle = "Practice: Complete & Publish Your Product Teardown",
            PracticeExercise = "Write and publish a 1,500-word product teardown on Substack or Notion. Share link for AI Mentor critique.",
            QuizTopic = "Product Case Study Framing & Executive Teardown",
            WhyItMatters = "A published product teardown proves your strategic acumen and communication skills before you even enter an interview.",
            YourEdge = "Recruiters and founders reach out directly to PMs who publish thoughtful, well-reasoned teardowns.",
            NextMove = "1. Write the teardown.\n2. Publish on Notion or Substack.\n3. Submit for AI Mentor review."
        },
        new()
        {
            Id = "pm-app-02",
            Order = 2,
            SequenceRole = "Next",
            EstimatedTime = "45 minutes",
            Title = "PM Interview Mastery: Product Sense, Execution & Behavioral Rounds",
            Url = "https://www.youtube.com/watch?v=d_kS_Z9k2f4",
            ThumbnailUrl = "https://img.youtube.com/vi/d_kS_Z9k2f4/hqdefault.jpg",
            Source = "YouTube · Exponent PM",
            ChannelOrAuthor = "Exponent",
            Duration = "45 min",
            Level = "Application",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Prepares you to master the rigorous Product Sense and Analytical Execution questions used at Google, Stripe, and leading startups.",
            WhatYouWillBuild = "An interview answer guide for 'Design X for Y' questions and 'How would you measure success for Z?' scenarios.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Structuring Product Sense Questions", Instruction = "Apply the CIRCLES framework: Clarify context, Identify persona, Report user needs, Cut through prioritization, List solutions, Evaluate tradeoffs, Summarize." },
                new() { StepNumber = 2, Title = "Answering Root-Cause Metric Drop Scenarios", Instruction = "Systematically diagnose unexpected metric drops by checking instrumentation bugs, seasonal factors, platform versions, and segment cohorts." },
                new() { StepNumber = 3, Title = "Behavioral Leadership Stories", Instruction = "Structure stories showing how you aligned disagreeing engineering and business stakeholders without direct authority." }
            ],
            PracticeExerciseTitle = "Practice: Answer 2 Mock Product Sense Questions",
            PracticeExercise = "Record a 5-minute answer to: 'Design a financial literacy product for teenagers in emerging markets.' Use CIRCLES framework.",
            QuizTopic = "Product Sense & Execution Interview Rounds",
            WhyItMatters = "Product sense interviews test your instincts, empathy, and structured thinking under real-time pressure.",
            YourEdge = "You structure ambiguous problems cleanly and always anchor recommendations in customer value.",
            NextMove = "1. Record the mock answer.\n2. Review against the rubric.\n3. Take the interview quiz."
        }
    ];

    // ─────────────────────────────────────────────────────────────────────────
    // 4. SOFTWARE ENGINEERING (Beginner → Intermediate → Advanced → Application)
    // ─────────────────────────────────────────────────────────────────────────

    private static List<LearningResource> SoftwareEngineeringResources() =>
    [
        // BEGINNER
        new()
        {
            Id = "swe-beg-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "45 minutes",
            Title = "Clean Code Architecture & Git Version Control Mastery",
            Url = "https://www.youtube.com/watch?v=RGOj5yH7evk",
            ThumbnailUrl = "https://img.youtube.com/vi/RGOj5yH7evk/hqdefault.jpg",
            Source = "YouTube · freeCodeCamp",
            ChannelOrAuthor = "freeCodeCamp.org",
            Duration = "45 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Git and clean code principles are mandatory for any software developer working on professional engineering teams.",
            WhatYouWillBuild = "A modular project repository with semantic commit history, feature branching, and automated linting checks.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Git Branching Workflows", Instruction = "Master feature branches, pull requests, interactive rebasing, and merge conflict resolution." },
                new() { StepNumber = 2, Title = "SOLID Principles in Practice", Instruction = "Apply Single Responsibility and Dependency Inversion to decouple business logic from infrastructure code." },
                new() { StepNumber = 3, Title = "Automated Formatting & Linting", Instruction = "Configure ESLint, Prettier, or Ruff to enforce uniform code styling across every commit." }
            ],
            PracticeExerciseTitle = "Practice: Initialize a Clean Git Repository",
            PracticeExercise = "Set up a repository with pre-commit hooks, write a small service adhering to SOLID principles, and submit a cleanly rebased pull request.",
            QuizTopic = "Git Workflows & SOLID Code Architecture",
            WhyItMatters = "Messy code and tangled Git histories create technical debt that slows development velocity to a crawl.",
            YourEdge = "You write self-documenting code that senior engineers can approve with minimal review comments.",
            NextMove = "1. Set up the repository.\n2. Configure pre-commit hooks.\n3. Take the checkpoint test."
        },

        // INTERMEDIATE
        new()
        {
            Id = "swe-int-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "50 minutes",
            Title = "RESTful API Design & Relational Database Indexing",
            Url = "https://www.youtube.com/watch?v=-MTSQjw5DrM",
            ThumbnailUrl = "https://img.youtube.com/vi/-MTSQjw5DrM/hqdefault.jpg",
            Source = "YouTube · Hussein Nasser",
            ChannelOrAuthor = "Hussein Nasser",
            Duration = "50 min",
            Level = "Intermediate",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Backend engineering revolves around designing robust APIs and writing queries that execute in milliseconds under high concurrency.",
            WhatYouWillBuild = "A secure REST API with JWT authentication, rate limiting, and optimized PostgreSQL indexes.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Designing Idempotent Endpoints", Instruction = "Use correct HTTP verbs, status codes, and idempotency keys to prevent duplicate transaction charges." },
                new() { StepNumber = 2, Title = "Database Indexing & Query Execution Plans", Instruction = "Inspect EXPLAIN ANALYZE output and add B-Tree indexes on foreign keys to eliminate sequential table scans." },
                new() { StepNumber = 3, Title = "Authentication & Rate Limiting", Instruction = "Implement JWT token validation and sliding-window rate limiting to protect endpoints from abuse." }
            ],
            PracticeExerciseTitle = "Practice: Optimize Slow Database Queries",
            PracticeExercise = "Given a slow 100k-row orders table, profile query latency with EXPLAIN ANALYZE, add the optimal composite index, and verify 10x latency drop.",
            QuizTopic = "REST Standards & PostgreSQL B-Tree Indexing",
            WhyItMatters = "Unindexed database queries will crash your server as soon as user traffic spikes.",
            YourEdge = "You know how databases execute queries on disk and can troubleshoot production latency bottlenecks quickly.",
            NextMove = "1. Profile the query.\n2. Add the index.\n3. Submit EXPLAIN output to AI Mentor."
        },

        // ADVANCED
        new()
        {
            Id = "swe-adv-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "55 minutes",
            Title = "Distributed Systems, Event-Driven Architecture & Message Queues",
            Url = "https://www.youtube.com/watch?v=r3vT_b_l5X0",
            ThumbnailUrl = "https://img.youtube.com/vi/r3vT_b_l5X0/hqdefault.jpg",
            Source = "YouTube · ByteByteGo",
            ChannelOrAuthor = "ByteByteGo",
            Duration = "55 min",
            Level = "Advanced",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Scalable internet platforms cannot rely on monolithic synchronous requests; distributed messaging guarantees high availability.",
            WhatYouWillBuild = "An asynchronous event-driven payment processing pipeline using RabbitMQ/Kafka with dead-letter queues and retry policies.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Synchronous vs Asynchronous Communication", Instruction = "Decouple high-traffic API requests from background jobs using pub/sub message brokers." },
                new() { StepNumber = 2, Title = "Idempotent Consumer Implementation", Instruction = "Ensure duplicate message deliveries do not trigger duplicate database balance updates." },
                new() { StepNumber = 3, Title = "Dead-Letter Queues & Circuit Breakers", Instruction = "Implement exponential backoff retry mechanisms and dead-letter queues for unrecoverable failures." }
            ],
            PracticeExerciseTitle = "Practice: Build an Idempotent Payment Worker",
            PracticeExercise = "Code a message consumer in Python or C# that processes payment events, checks idempotency keys, and routes failed jobs to a dead-letter queue.",
            QuizTopic = "Distributed Messaging & Event-Driven Patterns",
            WhyItMatters = "Guarantees zero lost transactions even when downstream third-party banking APIs experience downtime.",
            YourEdge = "You architect fault-tolerant distributed backends capable of processing thousands of events per second.",
            NextMove = "1. Code the consumer worker.\n2. Simulate message failure.\n3. Take the quiz."
        },

        // APPLICATION
        new()
        {
            Id = "swe-app-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "60 minutes",
            Title = "Full-Stack Production Capstone: Scalable SaaS with CI/CD & Cloud Deployment",
            Url = "https://www.youtube.com/watch?v=RGOj5yH7evk",
            ThumbnailUrl = "https://img.youtube.com/vi/RGOj5yH7evk/hqdefault.jpg",
            Source = "YouTube · Traversy Media",
            ChannelOrAuthor = "Brad Traversy",
            Duration = "60 min",
            Level = "Application",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Senior hiring managers look for engineers who can build from scratch, containerize with Docker, and automate deployments with CI/CD pipelines.",
            WhatYouWillBuild = "A deployed SaaS application with Docker containers, automated GitHub Actions testing pipeline, and live cloud hosting.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Full-Stack Application Architecture", Instruction = "Build frontend, REST API, and database services with clean separation of concerns." },
                new() { StepNumber = 2, Title = "Containerization with Docker Compose", Instruction = "Write multi-stage Dockerfiles and docker-compose.yml for local and production parity." },
                new() { StepNumber = 3, Title = "Automated CI/CD GitHub Actions", Instruction = "Configure automated workflows that run unit tests, security scans, and deploy on merge to main." },
                new() { StepNumber = 4, Title = "Production Monitoring & Health Checks", Instruction = "Add health check endpoints and error logging (Sentry) for real-time issue alerts." }
            ],
            PracticeExerciseTitle = "Practice: Deploy Your Application & Share the Live URL",
            PracticeExercise = "Deploy your containerized project to a cloud VPS or PaaS. Submit the live URL and GitHub repository link for AI Mentor review.",
            QuizTopic = "Production Deployment & CI/CD Pipelines",
            WhyItMatters = "Software that only runs on localhost has zero business value; delivering live software builds undeniable credibility.",
            YourEdge = "You can take any software idea from an empty folder to a live, secure cloud product with automated updates.",
            NextMove = "1. Push code to GitHub.\n2. Verify CI passes.\n3. Submit live URL for review."
        }
    ];

    // ─────────────────────────────────────────────────────────────────────────
    // 5. FINANCE, ENTREPRENEURSHIP & POLICY
    // ─────────────────────────────────────────────────────────────────────────

    private static List<LearningResource> FinanceAndFintechResources() =>
    [
        new()
        {
            Id = "fin-beg-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "45 minutes",
            Title = "Financial Statement Analysis & 3-Statement Modeling",
            Url = "https://www.youtube.com/watch?v=I6Y3F9wG6n8",
            ThumbnailUrl = "https://img.youtube.com/vi/I6Y3F9wG6n8/hqdefault.jpg",
            Source = "YouTube · RareLiquid",
            ChannelOrAuthor = "RareLiquid",
            Duration = "45 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Financial literacy begins with linking the Income Statement, Balance Sheet, and Cash Flow Statement dynamically.",
            WhatYouWillBuild = "A dynamic 3-statement financial model linking net income to operating cash flow and balance sheet equity.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Income Statement Revenue & Margin Flow", Instruction = "Model gross revenue, cost of goods sold, operating expenses (EBITDA), and net income." },
                new() { StepNumber = 2, Title = "Cash Flow Reconciliation", Instruction = "Reconcile net income to cash flow from operations by adjusting for non-cash depreciation and working capital shifts." },
                new() { StepNumber = 3, Title = "Balancing the Balance Sheet", Instruction = "Ensure total assets strictly equal total liabilities plus shareholders' equity via automated cash sweeps." }
            ],
            PracticeExerciseTitle = "Practice: Build a 3-Statement Model",
            PracticeExercise = "Take 3 years of audited financials of an African commercial bank or fintech. Build a linked 3-statement model.",
            QuizTopic = "3-Statement Financial Modeling",
            WhyItMatters = "Cash flow determines company survival; understanding financial statements allows you to evaluate company solvency and profitability.",
            YourEdge = "You understand how operational decisions impact working capital and free cash flow generation.",
            NextMove = "1. Build the model.\n2. Verify balance sheet balances.\n3. Take the quiz."
        },
        new()
        {
            Id = "fin-int-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "50 minutes",
            Title = "Discounted Cash Flow (DCF) Valuation & Sensitivity Analysis",
            Url = "https://www.youtube.com/watch?v=fd_pcS_xT8o",
            ThumbnailUrl = "https://img.youtube.com/vi/fd_pcS_xT8o/hqdefault.jpg",
            Source = "YouTube · Aswath Damodaran",
            ChannelOrAuthor = "Aswath Damodaran",
            Duration = "50 min",
            Level = "Intermediate",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "DCF valuation is the cornerstone of corporate finance and private equity investing.",
            WhatYouWillBuild = "A comprehensive DCF valuation model with Weighted Average Cost of Capital (WACC) and sensitivity matrices.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Forecasting Free Cash Flow to Firm (FCFF)", Instruction = "Project 5-year revenues, EBIT, tax rates, capital expenditures, and working capital requirements." },
                new() { StepNumber = 2, Title = "Calculating Cost of Capital (WACC)", Instruction = "Derive cost of equity using CAPM, country risk premiums, and after-tax cost of debt." },
                new() { StepNumber = 3, Title = "Terminal Value & Sensitivity Tables", Instruction = "Calculate terminal value using Gordon Growth and build 2D sensitivity tables across WACC and terminal growth rates." }
            ],
            PracticeExerciseTitle = "Practice: Value a High-Growth Fintech",
            PracticeExercise = "Value a regional payment gateway: forecast 5-year cash flows, apply a 14% emerging market WACC, and calculate implied share price.",
            QuizTopic = "DCF Valuation & WACC Calculations",
            WhyItMatters = "Price is what you pay; value is what you get. DCF analysis grounds investment decisions in economic reality.",
            YourEdge = "You evaluate company valuations based on fundamental cash flows rather than hype multiples.",
            NextMove = "1. Complete the valuation.\n2. Run sensitivity analysis.\n3. Take the quiz."
        },
        new()
        {
            Id = "fin-adv-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "45 minutes",
            Title = "Fintech Architecture: Payment Rails, Settlement & Core Banking",
            Url = "https://www.youtube.com/watch?v=r3vT_b_l5X0",
            ThumbnailUrl = "https://img.youtube.com/vi/r3vT_b_l5X0/hqdefault.jpg",
            Source = "YouTube · Modern Treasury",
            ChannelOrAuthor = "Modern Treasury",
            Duration = "45 min",
            Level = "Advanced",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Fintech engineers and product leaders must understand automated clearing houses (ACH), instant payment rails, and ledger integrity.",
            WhatYouWillBuild = "An immutable double-entry ledger architecture for multi-currency wallet transfers.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Double-Entry Ledger Design", Instruction = "Structure ledger entries where every transaction has balanced debits and credits across accounts." },
                new() { StepNumber = 2, Title = "Payment Reconciliation & Settlement Timing", Instruction = "Reconcile daily bank settlement files against internal transaction logs to detect discrepancies." },
                new() { StepNumber = 3, Title = "Handling Multi-Currency FX Spreads", Instruction = "Implement real-time FX rate locks and calculate realized vs unrealized foreign exchange gains." }
            ],
            PracticeExerciseTitle = "Practice: Design a Double-Entry Ledger",
            PracticeExercise = "Write the database schema for a double-entry ledger handling customer deposits, peer-to-peer transfers, and platform fees with zero balance drift.",
            QuizTopic = "Double-Entry Accounting & Payment Rails",
            WhyItMatters = "Single-entry balance tracking inevitably suffers from rounding errors and reconciliation failures.",
            YourEdge = "You design financial systems that pass rigorous central bank and financial regulatory audits.",
            NextMove = "1. Draft the ledger schema.\n2. Write transfer test cases.\n3. Take the quiz."
        },
        new()
        {
            Id = "fin-app-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "60 minutes",
            Title = "Fintech Investment Memo & Financial Due Diligence Capstone",
            Url = "https://www.youtube.com/watch?v=F3G8B87yXrk",
            ThumbnailUrl = "https://img.youtube.com/vi/F3G8B87yXrk/hqdefault.jpg",
            Source = "Venture Capital Memo Library",
            ChannelOrAuthor = "Venture Capital Memo Library",
            Duration = "60 min",
            Level = "Application",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Venture capitalists, investment bankers, and corporate strategy teams make capital allocation decisions through Investment Memos.",
            WhatYouWillBuild = "A 5-page Investment Committee Memo evaluating a high-growth fintech startup with financial models and risk mitigations.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Market Sizing & Total Addressable Market (TAM)", Instruction = "Calculate bottom-up TAM, serviceable addressable market (SAM), and market penetration velocity." },
                new() { StepNumber = 2, Title = "Unit Economics & Cohort Analysis", Instruction = "Analyze customer acquisition cost (CAC), lifetime value (LTV), payback period, and gross margin expansion." },
                new() { StepNumber = 3, Title = "Competitor Matrix & Regulatory Risk", Instruction = "Map direct and indirect competitors; analyze compliance requirements (licenses, capital reserves)." },
                new() { StepNumber = 4, Title = "Investment Recommendation & Terms", Instruction = "Propose investment valuation, liquidation preference, and governance board rights." }
            ],
            PracticeExerciseTitle = "Practice: Draft an Investment Committee Memo",
            PracticeExercise = "Write a complete Investment Memo on an African startup of your choice. Submit for AI Mentor review.",
            QuizTopic = "Venture Capital Investment Due Diligence",
            WhyItMatters = "Demonstrates high-level business reasoning, financial rigor, and executive communication.",
            YourEdge = "You evaluate startups with institutional-grade rigor.",
            NextMove = "1. Draft the memo.\n2. Review with AI Mentor.\n3. Add to your portfolio."
        }
    ];

    private static List<LearningResource> EntrepreneurshipResources() =>
    [
        new()
        {
            Id = "ent-beg-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "45 minutes",
            Title = "How to Evaluate Startup Ideas & Customer Discovery Interviews",
            Url = "https://www.youtube.com/watch?v=th3a6AtBiSA",
            ThumbnailUrl = "https://img.youtube.com/vi/th3a6AtBiSA/hqdefault.jpg",
            Source = "YouTube · Y Combinator",
            ChannelOrAuthor = "Y Combinator",
            Duration = "45 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Most startups fail because they build something nobody wants. Customer discovery interviews validate real demand before spending money.",
            WhatYouWillBuild = "A structured customer discovery interview script and problem validation score sheet.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Separating Problems from Solutions", Instruction = "Ask customers about their past behaviors and current workarounds rather than their opinions on your hypothetical idea." },
                new() { StepNumber = 2, Title = "Identifying Hair-on-Fire Problems", Instruction = "Find problems where customers are actively losing time or money and already trying to fix it with clumsy workarounds." },
                new() { StepNumber = 3, Title = "Synthesizing Insights into Value Propositions", Instruction = "Distill 10 customer interviews into a clear, differentiated value proposition." }
            ],
            PracticeExerciseTitle = "Practice: Conduct 3 Customer Discovery Interviews",
            PracticeExercise = "Interview 3 potential users for your product idea using The Mom Test guidelines. Document their existing workarounds.",
            QuizTopic = "Customer Discovery & Startup Idea Evaluation",
            WhyItMatters = "Validating customer demand early saves months of wasted coding and thousands of dollars.",
            YourEdge = "You build features people are desperate to pay for rather than guessing in isolation.",
            NextMove = "1. Conduct the interviews.\n2. Document insights.\n3. Take the quiz."
        },
        new()
        {
            Id = "ent-app-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "60 minutes",
            Title = "The Ultimate YC-Style Startup Pitch Deck & Investor Presentation",
            Url = "https://www.youtube.com/watch?v=pjyLpX54_7k",
            ThumbnailUrl = "https://img.youtube.com/vi/pjyLpX54_7k/hqdefault.jpg",
            Source = "YouTube · Y Combinator",
            ChannelOrAuthor = "Michael Seibel",
            Duration = "60 min",
            Level = "Application",
            Type = LearningResourceType.TutorialVideo,
            WhyAmILearningThis = "Fundraising requires presenting a compelling, concise narrative of huge market opportunity and early traction.",
            WhatYouWillBuild = "A 10-slide investor pitch deck covering Problem, Solution, Market Size, Traction, Business Model, and Team.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "1-Sentence Company Description", Instruction = "State what you do with total clarity in 10 words or fewer." },
                new() { StepNumber = 2, Title = "Traction & Moat Slides", Instruction = "Show evidence of demand: revenue growth, waitlists, retention cohorts, or signed letters of intent." },
                new() { StepNumber = 3, Title = "The Ask & Use of Funds", Instruction = "Clearly articulate how much capital you are raising and what milestones it will achieve in 18 months." }
            ],
            PracticeExerciseTitle = "Practice: Build Your 10-Slide Pitch Deck",
            PracticeExercise = "Create your pitch deck in Google Slides or Pitch.com. Submit link for AI Mentor investor pitch critique.",
            QuizTopic = "Startup Fundraising & Pitch Deck Structure",
            WhyItMatters = "A clear pitch deck secures angel and seed funding to turn an idea into an enduring company.",
            YourEdge = "You pitch with extreme clarity, avoiding buzzwords and focusing on traction metrics.",
            NextMove = "1. Build the deck.\n2. Submit for review.\n3. Rehearse pitch."
        }
    ];

    private static List<LearningResource> TechnologyPolicyResources() =>
    [
        new()
        {
            Id = "pol-beg-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "40 minutes",
            Title = "AI Governance, Data Sovereignty & National Tech Policy Frameworks",
            Url = "https://www.youtube.com/watch?v=F3G8B87yXrk",
            ThumbnailUrl = "https://img.youtube.com/vi/F3G8B87yXrk/hqdefault.jpg",
            Source = "Brookings Institution / Tech Policy",
            ChannelOrAuthor = "Brookings Institution",
            Duration = "40 min",
            Level = "Beginner",
            Type = LearningResourceType.BlogPost,
            WhyAmILearningThis = "Understanding regulatory landscapes, data localization, and AI safety frameworks is crucial for compliance and public policy careers.",
            WhatHappened = "Governments across Africa and Europe are enacting AI governance standards and cross-border data transfer regulations.",
            WhyItMatters = "Tech companies that fail to comply with data localization face severe fines or operational bans.",
            YourEdge = "You advise leadership on building products that comply with regional regulations from day one.",
            NextMove = "1. Read the framework summary.\n2. Check national data protection laws.\n3. Take the quiz."
        },
        new()
        {
            Id = "pol-app-01",
            Order = 1,
            SequenceRole = "Start here",
            EstimatedTime = "50 minutes",
            Title = "Writing an Actionable Policy Brief for Government Decision-Makers",
            Url = "https://www.youtube.com/watch?v=pjyLpX54_7k",
            ThumbnailUrl = "https://img.youtube.com/vi/pjyLpX54_7k/hqdefault.jpg",
            Source = "Harvard Kennedy School Policy Review",
            ChannelOrAuthor = "Harvard Kennedy School",
            Duration = "50 min",
            Level = "Application",
            Type = LearningResourceType.BlogPost,
            WhyAmILearningThis = "Policy analysts influence national legislation by turning complex technical topics into 2-page actionable briefs for ministers.",
            WhatHappened = "Modern policymakers demand concise policy memos structured around concrete legislative recommendations and stakeholder impacts.",
            WhyItMatters = "Academic papers are rarely read by ministers; executive policy briefs shape national technology strategies.",
            YourEdge = "You bridge technical engineering reality with regulatory and economic policy.",
            NextMove = "1. Draft a 2-page AI policy brief for your country's Ministry of Innovation.\n2. Submit for AI Mentor evaluation."
        }
    ];
}
