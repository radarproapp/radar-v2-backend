using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Curated catalog of high-utility learning resources spanning beginner to advanced levels.
/// Maintains an exact 80/20 distribution:
/// - 80% How-to and tutorial videos (hands-on step-by-step format)
/// - 20% Latest developments videos and blog posts (4-part intelligence format)
/// </summary>
public static class LearningResourceCatalog
{
    public static List<LearningResource> GetResourcesForSkill(string interestOrGoal, string? level = null)
    {
        var normalized = $"{interestOrGoal}".ToLowerInvariant();

        List<LearningResource> pool;
        if (normalized.Contains("ai") || normalized.Contains("machine learning") || normalized.Contains("artificial") || normalized.Contains("llm"))
            pool = AiAndMachineLearningResources();
        else if (normalized.Contains("data") || normalized.Contains("analytics") || normalized.Contains("sql") || normalized.Contains("bi"))
            pool = DataScienceResources();
        else if (normalized.Contains("product") || normalized.Contains("prd") || normalized.Contains("pm") || normalized.Contains("ux"))
            pool = ProductManagementResources();
        else if (normalized.Contains("finance") || normalized.Contains("fintech") || normalized.Contains("valuation") || normalized.Contains("banking") || normalized.Contains("invest"))
            pool = FinanceAndFintechResources();
        else if (normalized.Contains("entrepreneur") || normalized.Contains("startup") || normalized.Contains("venture") || normalized.Contains("business"))
            pool = EntrepreneurshipResources();
        else if (normalized.Contains("policy") || normalized.Contains("govern") || normalized.Contains("regulation"))
            pool = TechnologyPolicyResources();
        else
            pool = SoftwareEngineeringResources();

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
            _ => "Advanced"
        };
        return GetResourcesForSkill(interestOrGoal, level);
    }

    // ── AI & Machine Learning ────────────────────────────────────────────────

    private static List<LearningResource> AiAndMachineLearningResources() =>
    [
        // BEGINNER (80% Tutorials, 20% Latest Info)
        new()
        {
            Id = "ai-tut-01",
            Title = "Python for Machine Learning & AI – Complete Hands-On Beginner Course",
            Url = "https://www.youtube.com/watch?v=i_LwzRVP7bg",
            ThumbnailUrl = "https://img.youtube.com/vi/i_LwzRVP7bg/hqdefault.jpg",
            Source = "YouTube · freeCodeCamp",
            ChannelOrAuthor = "freeCodeCamp.org",
            Duration = "48 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A clean Python machine learning development environment and a working classification script predicting real-world outcomes.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Environment Setup & Virtualenv", Instruction = "Install Python 3.11+, set up a virtual environment, and configure Jupyter Notebook with essential scientific libraries (NumPy, Pandas, Matplotlib)." },
                new() { StepNumber = 2, Title = "Data Exploration & Feature Structuring", Instruction = "Load a raw tabular dataset, inspect data types, clean missing values, and structure inputs into training matrices X and target vectors y." },
                new() { StepNumber = 3, Title = "Model Instantiation & Training", Instruction = "Fit your first Scikit-Learn baseline estimator, configure hyperparameters, and train the model on structured training batches." },
                new() { StepNumber = 4, Title = "Evaluation & Test Inference", Instruction = "Run test set predictions, generate a confusion matrix, and calculate accuracy, precision, and recall scores." }
            ],
            WhyItMatters = "Mastering foundational data ingestion and model fitting is the non-negotiable bedrock before advancing to deep learning and LLM architectures.",
            YourEdge = "You bridge theoretical ML concepts into clean, reproducible Python code that runs locally on commodity hardware without cloud lock-in.",
            NextMove = "1. Clone the starter repository.\n2. Ingest a Kaggle telecom churn dataset.\n3. Train and compare Logistic Regression vs Random Forest baselines."
        },
        new()
        {
            Id = "ai-tut-02",
            Title = "Building Your First Neural Network from Scratch in PyTorch",
            Url = "https://www.youtube.com/watch?v=GIsg-ZUy0MY",
            ThumbnailUrl = "https://img.youtube.com/vi/GIsg-ZUy0MY/hqdefault.jpg",
            Source = "YouTube · Aladdin Persson",
            ChannelOrAuthor = "Aladdin Persson",
            Duration = "36 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A modular multi-layer perceptron (MLP) in PyTorch that classifies structured customer transactions.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "PyTorch Tensor Fundamentals", Instruction = "Understand PyTorch tensors, memory allocation, and autograd computation graphs for backpropagation." },
                new() { StepNumber = 2, Title = "Building the nn.Module Architecture", Instruction = "Define input layers, linear transformations, ReLU activation functions, and output probability dimensions." },
                new() { StepNumber = 3, Title = "Writing the Training Loop", Instruction = "Implement the canonical zero_grad -> forward -> loss -> backward -> optimizer.step() loop with CrossEntropyLoss." },
                new() { StepNumber = 4, Title = "Tracking Validation Loss & Checkpointing", Instruction = "Log validation metrics per epoch and save the best model weights to disk." }
            ],
            WhyItMatters = "Understanding the PyTorch autograd engine unlocks the ability to customize architectures and debug training instability.",
            YourEdge = "Unlike engineers who only call black-box APIs, you understand gradient backpropagation and loss convergence at the mathematical layer.",
            NextMove = "1. Implement a 3-layer MLP in PyTorch.\n2. Add Dropout layers to mitigate overfitting.\n3. Save and reload the trained .pt checkpoint."
        },
        new()
        {
            Id = "ai-tut-03",
            Title = "Prompt Engineering & Structured Outputs with OpenAI and DeepSeek",
            Url = "https://www.youtube.com/watch?v=jC4v5AS4RIM",
            ThumbnailUrl = "https://img.youtube.com/vi/jC4v5AS4RIM/hqdefault.jpg",
            Source = "YouTube · AssemblyAI",
            ChannelOrAuthor = "AssemblyAI",
            Duration = "28 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A deterministic JSON extraction pipeline that parses unstructured African trade reports into strict database schemas.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "System Prompt Framing", Instruction = "Structure clear system constraints, persona framing, and negative guardrails to eliminate hallucination." },
                new() { StepNumber = 2, Title = "Few-Shot Example Injection", Instruction = "Supply 2-3 canonical input/output pairs in the prompt to ground edge cases and rare formats." },
                new() { StepNumber = 3, Title = "JSON Schema Enforcement", Instruction = "Configure response_format with JSON schemas to guarantee parseable, type-safe program outputs." },
                new() { StepNumber = 4, Title = "Error Recovery & Validation", Instruction = "Implement Pydantic schema validation with automatic fallback retries upon schema validation failures." }
            ],
            WhyItMatters = "Production LLM apps fail when models output conversational fluff instead of rigid, machine-readable JSON schemas.",
            YourEdge = "You can reliably embed LLM intelligence into existing enterprise backend pipelines and databases without breaking JSON parsers.",
            NextMove = "1. Build a prompt parsing PDF press releases.\n2. Enforce strict JSON output containing date, company, and valuation.\n3. Test with edge-case documents."
        },
        new()
        {
            Id = "ai-tut-04",
            Title = "Building a Fast Vector Search & Embeddings Pipeline with ChromaDB",
            Url = "https://www.youtube.com/watch?v=tcqEUSNCn8I",
            ThumbnailUrl = "https://img.youtube.com/vi/tcqEUSNCn8I/hqdefault.jpg",
            Source = "YouTube · James Briggs",
            ChannelOrAuthor = "James Briggs",
            Duration = "32 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A semantic document retrieval engine that matches user queries to policy documents using cosine similarity.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Document Chunking Strategies", Instruction = "Chunk lengthy documents using recursive character splitters with sentence-boundary awareness and 15% overlap." },
                new() { StepNumber = 2, Title = "Vector Embedding Generation", Instruction = "Convert text chunks into dense 768-dimensional vector representations using local sentence-transformers." },
                new() { StepNumber = 3, Title = "ChromaDB Collection Indexing", Instruction = "Persist vectors into a local ChromaDB collection along with rich metadata tags (source, author, timestamp)." },
                new() { StepNumber = 4, Title = "Query Execution & Re-ranking", Instruction = "Execute cosine distance semantic queries and retrieve the top-5 most relevant passages." }
            ],
            WhyItMatters = "Vector search is the foundational engine beneath RAG (Retrieval-Augmented Generation) and enterprise knowledge retrieval.",
            YourEdge = "You can make any internal unstructured library instantly searchable by meaning rather than brittle keyword matching.",
            NextMove = "1. Ingest 10 local PDF reports into ChromaDB.\n2. Query with natural language questions.\n3. Measure retrieval precision vs classic keyword search."
        },
        // LATEST DEVELOPMENTS & BLOGS (20%)
        new()
        {
            Id = "ai-dev-01",
            Title = "State of AI 2026: Reasoning Models, Small Language Models & Edge Inference",
            Url = "https://www.youtube.com/watch?v=zjkBMFhNj_g",
            ThumbnailUrl = "https://img.youtube.com/vi/zjkBMFhNj_g/hqdefault.jpg",
            Source = "YouTube · Andrej Karpathy",
            ChannelOrAuthor = "Andrej Karpathy",
            Duration = "42 min",
            Level = "Beginner",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhatHappened = "Andrej Karpathy explores the transition from raw next-token prediction to test-time reasoning and ultra-compact small language models (SLMs) running on edge devices.",
            WhyItMatters = "Understanding the architectural shift towards test-time compute allows you to build cost-effective AI features without relying on massive million-dollar GPU clusters.",
            YourEdge = "You design lean, hybrid AI systems that run lightweight 7B/14B reasoning models locally, drastically cutting inference bills while maintaining quality.",
            NextMove = "1. Read Karpathy's breakdown on test-time compute.\n2. Benchmark DeepSeek-R1-Distill-Qwen-7B against standard models.\n3. Evaluate latency for local customer service bots."
        },

        // INTERMEDIATE (80% Tutorials, 20% Latest Info)
        new()
        {
            Id = "ai-tut-05",
            Title = "Production RAG with Hybrid Search, Re-ranking & Citations",
            Url = "https://www.youtube.com/watch?v=sVcwVQRHIc8",
            ThumbnailUrl = "https://img.youtube.com/vi/sVcwVQRHIc8/hqdefault.jpg",
            Source = "YouTube · Greg Kamradt",
            ChannelOrAuthor = "Greg Kamradt",
            Duration = "52 min",
            Level = "Intermediate",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "An enterprise RAG system combining BM25 keyword search, dense vector retrieval, cross-encoder re-ranking, and automated source citation.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Hybrid Search Implementation", Instruction = "Combine sparse BM25 lexical search with dense vector embeddings via Reciprocal Rank Fusion (RRF)." },
                new() { StepNumber = 2, Title = "Cross-Encoder Re-Ranking", Instruction = "Pass the top 25 hybrid candidate passages through a Cohere or BGE cross-encoder to produce the definitive top 5 chunks." },
                new() { StepNumber = 3, Title = "Prompt Injection with Source Markers", Instruction = "Format the prompt context with bracketed source identifiers ([Source 1: Policy Page 14]) to enforce citation hygiene." },
                new() { StepNumber = 4, Title = "Hallucination & Faithfulness Evaluation", Instruction = "Implement Ragas metrics to score answer relevance and ground truth alignment programmatically." }
            ],
            WhyItMatters = "Naïve vector search retrieves irrelevant context 30% of the time; hybrid search with re-ranking eliminates enterprise hallucination.",
            YourEdge = "You can build mission-critical document assistants for legal, banking, and medical domains where accuracy and auditability are non-negotiable.",
            NextMove = "1. Build an RRF hybrid pipeline in Python.\n2. Test against a 100-page banking regulation PDF.\n3. Verify every model claim links to an exact paragraph citation."
        },
        new()
        {
            Id = "ai-tut-06",
            Title = "Building Autonomous AI Agents with LangGraph & Tool Calling",
            Url = "https://www.youtube.com/watch?v=1bPuqDcwV5w",
            ThumbnailUrl = "https://img.youtube.com/vi/1bPuqDcwV5w/hqdefault.jpg",
            Source = "YouTube · LangChain",
            ChannelOrAuthor = "LangChain",
            Duration = "45 min",
            Level = "Intermediate",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A multi-step autonomous market intelligence agent with state management, conditional edges, web search, and SQLite tools.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "StateGraph Schema Definition", Instruction = "Define the TypedDict agent state holding conversation history, tool outputs, and execution decision flags." },
                new() { StepNumber = 2, Title = "Equipping External Tool Nodes", Instruction = "Define deterministic Python functions for HTTP web requests, currency conversion, and database queries with docstrings." },
                new() { StepNumber = 3, Title = "Configuring Conditional Edges", Instruction = "Write router functions that inspect LLM tool call requests and branch to either tool execution or user response." },
                new() { StepNumber = 4, Title = "State Persistence & Human-in-the-Loop", Instruction = "Add MemorySaver checkpointing to allow human inspection and approval before executing sensitive financial actions." }
            ],
            WhyItMatters = "Stateless chatbots cannot execute complex, multi-day workflows; stateful cyclical graphs are the industry standard for production agentic systems.",
            YourEdge = "You construct autonomous software agents that don't get stuck in infinite hallucination loops and can interact reliably with external APIs.",
            NextMove = "1. Implement a 3-node LangGraph agent.\n2. Add a weather and stock pricing tool.\n3. Verify state persistence across conversation turns."
        },
        new()
        {
            Id = "ai-tut-07",
            Title = "Fine-Tuning Open Source LLMs with LoRA & Unsloth",
            Url = "https://www.youtube.com/watch?v=Us58X09k_eo",
            ThumbnailUrl = "https://img.youtube.com/vi/Us58X09k_eo/hqdefault.jpg",
            Source = "YouTube · Umar Jamil",
            ChannelOrAuthor = "Umar Jamil",
            Duration = "55 min",
            Level = "Intermediate",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A domain-specialized 8B parameter model fine-tuned on African corporate contracts using QLoRA in a single free Google Colab GPU.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Domain Dataset Preparation", Instruction = "Format domain text into alpaca or ShareGPT conversation JSONL pairs with clean instruction-response formatting." },
                new() { StepNumber = 2, Title = "4-bit Quantization & LoRA Target Adapters", Instruction = "Load base weights with BitsAndBytes 4-bit precision and inject low-rank adapters into attention projection matrices." },
                new() { StepNumber = 3, Title = "Training Loop with Gradient Accumulation", Instruction = "Configure Unsloth / Hugging Face SFTTrainer with cosine learning rate schedule and FP16 mixed precision." },
                new() { StepNumber = 4, Title = "Adapter Merging & GGUF Export", Instruction = "Merge LoRA adapter weights back into base 16-bit weights and export to GGUF format for ultra-fast local Ollama inference." }
            ],
            WhyItMatters = "Proprietary model APIs cost thousands per month; a fine-tuned open model costs pennies, keeps IP private, and excels at niche domain terminology.",
            YourEdge = "You can deliver high-performance private AI solutions to financial and government institutions that cannot send data to public US cloud APIs.",
            NextMove = "1. Prepare 200 question-answer pairs for your chosen domain.\n2. Run a 3-epoch QLoRA fine-tuning run in Google Colab.\n3. Benchmark before vs after accuracy."
        },
        new()
        {
            Id = "ai-tut-08",
            Title = "Deploying Real-Time FastEmbed & Ollama Services with FastAPI and Docker",
            Url = "https://www.youtube.com/watch?v=7v_7k_7Qp6o",
            ThumbnailUrl = "https://img.youtube.com/vi/7v_7k_7Qp6o/hqdefault.jpg",
            Source = "YouTube · Tech With Tim",
            ChannelOrAuthor = "Tech With Tim",
            Duration = "38 min",
            Level = "Intermediate",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A scalable, production-ready inference microservice with streaming SSE endpoints, health checks, and Docker containerization.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "FastAPI Asynchronous Endpoint Setup", Instruction = "Initialize FastAPI with lifespan events, Pydantic request models, and CORS security headers." },
                new() { StepNumber = 2, Title = "Streaming Response Implementation", Instruction = "Implement asynchronous generators yielding Server-Sent Events (SSE) token chunks as tokens stream from the model." },
                new() { StepNumber = 3, Title = "Docker Multi-Stage Build", Instruction = "Write a production Dockerfile minimizing image footprint and pre-downloading model weights during container initialization." },
                new() { StepNumber = 4, Title = "Rate Limiting & Health Monitoring", Instruction = "Add redis-based token bucket rate limiting and Prometheus metrics instrumentation." }
            ],
            WhyItMatters = "Jupyter notebooks don't serve traffic; packaging AI pipelines into rock-solid Docker containers is what turns models into real products.",
            YourEdge = "You can take any AI prototype and ship it as a production API that mobile and web apps can consume with low latency.",
            NextMove = "1. Containerize a FastAPI streaming inference app.\n2. Run Docker Compose locally.\n3. Test streaming chunk delivery with curl."
        },
        // LATEST DEVELOPMENTS & BLOGS (20%)
        new()
        {
            Id = "ai-dev-02",
            Title = "The Shift from Prompt Engineering to Context Engineering & Memory Architecture",
            Url = "https://latent.space/p/context-engineering",
            ThumbnailUrl = "https://images.unsplash.com/photo-1618005182384-a83a8bd57fbe?w=800&q=80",
            Source = "Blog Essay · Latent Space",
            ChannelOrAuthor = "Swyx & Alessio Fanelli",
            Duration = "14 min read",
            Level = "Intermediate",
            Type = LearningResourceType.BlogPost,
            WhatHappened = "Analysis of how top AI engineering teams moved past clever prompt wording to dynamic context caching, hierarchical retrieval, and structured key-value memory.",
            WhyItMatters = "Context engineering determines 90% of model performance in complex applications with multi-turn user memory and sprawling domain data.",
            YourEdge = "You structure context windows cleanly with compression and cache-control breakpoints, saving up to 80% on OpenAI and Anthropic token bills.",
            NextMove = "1. Implement Anthropic / DeepSeek prompt caching headers.\n2. Measure latency and token cost reduction on repetitive system prompts."
        },

        // ADVANCED (80% Tutorials, 20% Latest Info)
        new()
        {
            Id = "ai-tut-09",
            Title = "Building Multi-Agent Systems with Swarm and AutoGen for Complex Financial Research",
            Url = "https://www.youtube.com/watch?v=zwtg8q3qE8g",
            ThumbnailUrl = "https://img.youtube.com/vi/zwtg8q3qE8g/hqdefault.jpg",
            Source = "YouTube · Matthew Berman",
            ChannelOrAuthor = "Matthew Berman",
            Duration = "46 min",
            Level = "Advanced",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A multi-agent research collective featuring a Lead Researcher, a Quantitative Analyst, and a Skeptical Fact-Checker producing institutional-grade memos.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Specialized Agent Persona & Boundary Definition", Instruction = "Define strict operational scopes and distinct tool allocations for each agent to prevent conversational deadlock." },
                new() { StepNumber = 2, Title = "Handoff Routine Implementation", Instruction = "Implement functional handoffs allowing the Lead Researcher to delegate sub-tasks to the Quantitative Analyst with structured parameters." },
                new() { StepNumber = 3, Title = "Adversarial Critique & Verification Cycle", Instruction = "Route generated findings through the Skeptical Fact-Checker agent to challenge assertions against raw source data." },
                new() { StepNumber = 4, Title = "Synthesis & Artifact Compilation", Instruction = "Compile the verified multi-agent findings into a final PDF/Markdown report complete with methodology notes." }
            ],
            WhyItMatters = "Single agents struggle with complex multi-perspective tasks; multi-agent collaboration with built-in critique reduces hallucination by over 70%.",
            YourEdge = "You can orchestrate autonomous intelligence teams that conduct research in minutes that previously took entire teams of human analysts days.",
            NextMove = "1. Set up a 3-agent research team in Python.\n2. Task them with analyzing financial reports of a publicly traded African company.\n3. Validate the fact-checker caught inconsistencies."
        },
        new()
        {
            Id = "ai-tut-10",
            Title = "Full Custom Training: Tokenizer, RoPE Embeddings & Transformer Architecture from Scratch",
            Url = "https://www.youtube.com/watch?v=kCc8FmEb1nY",
            ThumbnailUrl = "https://img.youtube.com/vi/kCc8FmEb1nY/hqdefault.jpg",
            Source = "YouTube · Andrej Karpathy",
            ChannelOrAuthor = "Andrej Karpathy",
            Duration = "118 min",
            Level = "Advanced",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A complete GPT-style decoder-only transformer with Byte-Pair Encoding (BPE), Rotary Position Embeddings (RoPE), and Multi-Head Attention coded from scratch in pure PyTorch.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Byte-Pair Encoding Tokenizer Training", Instruction = "Train a custom BPE tokenizer on domain text and handle vocabulary merges and compression ratio optimization." },
                new() { StepNumber = 2, Title = "Causal Multi-Head Attention with RoPE", Instruction = "Implement query, key, value matrix projections, rotary positional embeddings, and masked causal attention score calculation." },
                new() { StepNumber = 3, Title = "Feed-Forward SwiGLU Network & RMSNorm", Instruction = "Construct modern SwiGLU non-linearities and pre-layer normalization with RMSNorm for training stability." },
                new() { StepNumber = 4, Title = "Autoregressive Sampling & Top-k/Top-p Nucleus Generation", Instruction = "Write top-p nucleus sampling and temperature scaling routines for fluent autoregressive text generation." }
            ],
            WhyItMatters = "Knowing every matrix multiplication inside the attention mechanism is what separates elite AI engineers from surface-level API wrappers.",
            YourEdge = "You can diagnose deep model failures, optimize GPU kernel performance, and build custom architectures for specialized hardware.",
            NextMove = "1. Code the causal attention block from memory.\n2. Train a mini 10M parameter language model on TinyStories.\n3. Verify generation quality."
        },
        new()
        {
            Id = "ai-tut-11",
            Title = "Optimizing LLM Inference with vLLM, PagedAttention & Tensor Parallelism",
            Url = "https://www.youtube.com/watch?v=Wz7n9Xy1g8Y",
            ThumbnailUrl = "https://img.youtube.com/vi/Wz7n9Xy1g8Y/hqdefault.jpg",
            Source = "YouTube · Trelis Research",
            ChannelOrAuthor = "Trelis Research",
            Duration = "40 min",
            Level = "Advanced",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A high-throughput inference cluster serving 100+ concurrent requests using vLLM PagedAttention and continuous batching.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "KV Cache Memory Bottleneck Analysis", Instruction = "Profile how traditional attention wastes 60-80% of GPU VRAM through memory fragmentation during variable-length generation." },
                new() { StepNumber = 2, Title = "PagedAttention Virtual Memory Mapping", Instruction = "Configure vLLM's virtual memory block tables to allocate KV cache dynamically without pre-allocated padding." },
                new() { StepNumber = 3, Title = "Continuous Batching Benchmarking", Instruction = "Benchmark request throughput under continuous iteration-level scheduling vs static batching." },
                new() { StepNumber = 4, Title = "Multi-GPU Tensor Parallelism Setup", Instruction = "Deploy model shards across multiple GPU devices using NCCL communication primitives." }
            ],
            WhyItMatters = "Inference efficiency directly impacts unit economics; vLLM delivers 5x to 10x throughput on the exact same GPU hardware.",
            YourEdge = "You can drastically reduce the cloud hosting cost of large-scale AI applications while improving p99 response latencies.",
            NextMove = "1. Spin up a vLLM container on a GPU instance.\n2. Run a concurrent load test using Locust.\n3. Compare throughput against standard HuggingFace pipeline."
        },
        new()
        {
            Id = "ai-tut-12",
            Title = "Reinforcement Learning from Human Feedback (RLHF) and DPO Implementation",
            Url = "https://www.youtube.com/watch?v=e_Q7n0_tq2w",
            ThumbnailUrl = "https://img.youtube.com/vi/e_Q7n0_tq2w/hqdefault.jpg",
            Source = "YouTube · Yannic Kilcher",
            ChannelOrAuthor = "Yannic Kilcher",
            Duration = "50 min",
            Level = "Advanced",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A Direct Preference Optimization (DPO) pipeline aligning an open-source model to human safety, formatting, and tone preferences without a separate reward model.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Preference Dataset Structuring", Instruction = "Prepare prompt-chosen-rejected triples that cleanly capture desired vs undesired model behaviors." },
                new() { StepNumber = 2, Title = "Reference Model Initialization", Instruction = "Freeze an identical reference model to compute the implicit reward difference and calculate KL divergence." },
                new() { StepNumber = 3, Title = "DPO Loss Function Implementation", Instruction = "Implement the binary cross-entropy loss over the log ratio of policy probabilities to reference probabilities." },
                new() { StepNumber = 4, Title = "Safety & Alignment Evaluation", Instruction = "Evaluate win-rate against the unaligned base model using automated LLM-as-a-judge scoring." }
            ],
            WhyItMatters = "DPO has replaced complicated PPO-based RLHF as the standard way modern models like Llama 3 and DeepSeek are aligned.",
            YourEdge = "You can customize model behavior, personality, and guardrails to match exact brand or institutional requirements.",
            NextMove = "1. Structure 50 preference pairs in JSON.\n2. Run a DPO training run using TRL (Transformer Reinforcement Learning).\n3. Evaluate how model answers change on difficult prompts."
        },
        // LATEST DEVELOPMENTS & BLOGS (20%)
        new()
        {
            Id = "ai-dev-03",
            Title = "DeepSeek-R1 Architecture Breakdown: Pure RL, Cold-Start Data & Incentive Alignment",
            Url = "https://www.youtube.com/watch?v=3c_U2A7_Gyo",
            ThumbnailUrl = "https://img.youtube.com/vi/3c_U2A7_Gyo/hqdefault.jpg",
            Source = "YouTube · AI Explained",
            ChannelOrAuthor = "AI Explained",
            Duration = "26 min",
            Level = "Advanced",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhatHappened = "Deep dive into how DeepSeek developed competitive reasoning models using pure reinforcement learning without massive supervised fine-tuning, dramatically reducing training costs.",
            WhyItMatters = "Proves that computational reasoning emerges through rule-based RL reward shaping (format compliance + math correctness) rather than raw brute-force scale.",
            YourEdge = "You can train task-specific reasoning capabilities into small models using verifiable reward functions rather than expensive human annotation.",
            NextMove = "1. Read the DeepSeek-R1 technical report.\n2. Note the rule-based verification approach for code and math.\n3. Design a verifiable reward function for an internal domain task."
        }
    ];

    // ── Data Science & Analytics ─────────────────────────────────────────────

    private static List<LearningResource> DataScienceResources() =>
    [
        new()
        {
            Id = "data-tut-01",
            Title = "Complete SQL for Data Analytics – Zero to Hero Tutorial",
            Url = "https://www.youtube.com/watch?v=HXV3zeRR3h4",
            ThumbnailUrl = "https://img.youtube.com/vi/HXV3zeRR3h4/hqdefault.jpg",
            Source = "YouTube · freeCodeCamp",
            ChannelOrAuthor = "Luke Barousse",
            Duration = "55 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A complete analytics query suite extracting revenue, cohort retention, and customer lifetime value from transactional PostgreSQL tables.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Database Schema & Ingestion", Instruction = "Set up PostgreSQL and import transactional e-commerce order tables." },
                new() { StepNumber = 2, Title = "Aggregations & Groupings", Instruction = "Write multi-level GROUP BY, HAVING, and date-truncation queries." },
                new() { StepNumber = 3, Title = "Multi-Table Relational Joins", Instruction = "Join users, orders, and products tables with INNER and LEFT joins while avoiding duplicate row traps." },
                new() { StepNumber = 4, Title = "Common Table Expressions (CTEs)", Instruction = "Structure clean, readable analytical queries using WITH clauses." }
            ],
            WhyItMatters = "SQL is the universal query language of business intelligence; every analytical workflow starts with raw relational tables.",
            YourEdge = "You write clean, highly readable SQL queries that execute quickly on million-row databases.",
            NextMove = "1. Install PostgreSQL.\n2. Query customer churn rates by signup month.\n3. Calculate month-over-month revenue growth."
        },
        new()
        {
            Id = "data-tut-02",
            Title = "Python Pandas & NumPy Crash Course for Data Science",
            Url = "https://www.youtube.com/watch?v=vmEHCJofslg",
            ThumbnailUrl = "https://img.youtube.com/vi/vmEHCJofslg/hqdefault.jpg",
            Source = "YouTube · Keith Galli",
            ChannelOrAuthor = "Keith Galli",
            Duration = "58 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "An end-to-end data transformation pipeline cleaning messy real-world African telecom and mobile money CSV exports.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "DataFrame Loading & Inspection", Instruction = "Read diverse file formats (CSV, Excel, JSON) and inspect missing values with info() and describe()." },
                new() { StepNumber = 2, Title = "Filtering & Vectorized Column Transformations", Instruction = "Perform high-speed vectorized column calculations and conditional row slicing without slow loops." },
                new() { StepNumber = 3, Title = "Handling Missing & Inconsistent Values", Instruction = "Impute or drop missing entries and normalize date and currency strings with regex." },
                new() { StepNumber = 4, Title = "Exporting Clean Analytical Datasets", Instruction = "Write partitioned, optimized Parquet files ready for BI dashboards." }
            ],
            WhyItMatters = "80% of data science work is data wrangling; mastering Pandas makes you 5x faster than analysts struggling with manual spreadsheets.",
            YourEdge = "You can process large messy datasets in seconds with reproducible scripts rather than error-prone manual Excel edits.",
            NextMove = "1. Download a raw regional dataset.\n2. Write a 20-line cleaning pipeline.\n3. Export as Parquet."
        },
        new()
        {
            Id = "data-tut-03",
            Title = "Interactive Data Dashboards with Streamlit & Plotly",
            Url = "https://www.youtube.com/watch?v=VqgUkExPvLY",
            ThumbnailUrl = "https://img.youtube.com/vi/VqgUkExPvLY/hqdefault.jpg",
            Source = "YouTube · Data Professor",
            ChannelOrAuthor = "Data Professor",
            Duration = "34 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A live interactive analytics web dashboard allowing executive stakeholders to filter metrics by region, date, and product tier.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Streamlit App Layout & Widgets", Instruction = "Build sidebar filters with dropdowns, sliders, and date pickers in pure Python." },
                new() { StepNumber = 2, Title = "Dynamic Plotly Chart Rendering", Instruction = "Create responsive interactive scatter plots, choropleth maps, and time-series line charts." },
                new() { StepNumber = 3, Title = "Data Caching with @st.cache_data", Instruction = "Cache database query results in memory to keep dashboard interactions instant." },
                new() { StepNumber = 4, Title = "One-Click Cloud Deployment", Instruction = "Deploy the dashboard to Streamlit Community Cloud with GitHub integration." }
            ],
            WhyItMatters = "Insights hidden in code help nobody; interactive dashboards make data actionable for executives, clients, and partners.",
            YourEdge = "You can transform raw analytical findings into polished web applications in an afternoon without hiring frontend developers.",
            NextMove = "1. Build a 2-page Streamlit dashboard.\n2. Connect to an open financial dataset.\n3. Deploy and share the public link."
        },
        new()
        {
            Id = "data-dev-01",
            Title = "The Rise of Polars & DuckDB: Why Modern Data Teams Are Moving Beyond Pandas",
            Url = "https://www.youtube.com/watch?v=mi9f9zOaqM8",
            ThumbnailUrl = "https://img.youtube.com/vi/mi9f9zOaqM8/hqdefault.jpg",
            Source = "YouTube · Alex The Analyst",
            ChannelOrAuthor = "Alex The Analyst",
            Duration = "22 min",
            Level = "Beginner",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhatHappened = "Overview of the industry shift towards Rust-powered columnar engines (Polars and DuckDB) that execute queries 20x faster than legacy tools.",
            WhyItMatters = "Enables analysts to process 50GB+ datasets on a standard laptop without spinning up expensive cloud Snowflake or Databricks clusters.",
            YourEdge = "You dramatically slash cloud data warehouse bills by running local serverless OLAP queries with zero infrastructure overhead.",
            NextMove = "1. Install DuckDB.\n2. Query a 1GB CSV file directly using SQL.\n3. Measure query execution speed vs Pandas."
        },
        // INTERMEDIATE & ADVANCED DATA
        new()
        {
            Id = "data-tut-04",
            Title = "Advanced SQL Window Functions, Cohorts & Retention Modeling",
            Url = "https://www.youtube.com/watch?v=Ww71knvhQ-s",
            ThumbnailUrl = "https://img.youtube.com/vi/Ww71knvhQ-s/hqdefault.jpg",
            Source = "YouTube · Alex The Analyst",
            ChannelOrAuthor = "Alex The Analyst",
            Duration = "44 min",
            Level = "Intermediate",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "An advanced cohort retention matrix and running balance model using PARTITION BY, LEAD, LAG, and DENSE_RANK.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Window Function Syntax & Framing", Instruction = "Master OVER (PARTITION BY ... ORDER BY ... ROWS BETWEEN ...)." },
                new() { StepNumber = 2, Title = "Customer Cohort Assignment", Instruction = "Calculate each user's signup month and subsequent activity delta." },
                new() { StepNumber = 3, Title = "Building the Retention Matrix", Instruction = "Pivot month-by-month user counts to measure 30/60/90-day retention." },
                new() { StepNumber = 4, Title = "Running Totals & Moving Averages", Instruction = "Compute 7-day smoothed revenue trends and cumulative customer counts." }
            ],
            WhyItMatters = "Cohort retention is the #1 metric venture capitalists and growth leaders care about to assess product-market fit.",
            YourEdge = "You can diagnose churn issues and model unit economics directly in SQL at an institutional level.",
            NextMove = "1. Write a cohort retention query on sample user logs.\n2. Calculate 30-day repeat purchase rate.\n3. Graph the retention curve."
        }
    ];

    // ── Product Management ───────────────────────────────────────────────────

    private static List<LearningResource> ProductManagementResources() =>
    [
        new()
        {
            Id = "pm-tut-01",
            Title = "How to Write a Great Product Requirements Document (PRD)",
            Url = "https://www.youtube.com/watch?v=xZ_u5q9dY4Q",
            ThumbnailUrl = "https://img.youtube.com/vi/xZ_u5q9dY4Q/hqdefault.jpg",
            Source = "YouTube · Lenny's Podcast",
            ChannelOrAuthor = "Lenny Rachitsky",
            Duration = "36 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A comprehensive 1-page PRD defining problem statement, non-goals, user user journeys, and telemetry metrics.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Problem Framing & Non-Goals", Instruction = "Isolate the exact user problem with evidence, and explicitly list what is out of scope." },
                new() { StepNumber = 2, Title = "User Personas & Journey Steps", Instruction = "Map the end-to-end user journey across discovery, action, and confirmation states." },
                new() { StepNumber = 3, Title = "Functional Specifications & Edge Cases", Instruction = "Detail feature requirements, loading states, error boundaries, and permission rules." },
                new() { StepNumber = 4, Title = "Success Metrics & Launch Guardrails", Instruction = "Define primary KPIs, secondary guardrail metrics, and rollout plan." }
            ],
            WhyItMatters = "Vague feature ideas cause engineering delays; a crisp PRD aligns engineering, design, and executives in one read.",
            YourEdge = "You prevent scope creep and rally cross-functional teams around clear, measurable product outcomes.",
            NextMove = "1. Pick a feature from an app you use daily.\n2. Write a 1-page PRD addressing one critical flaw.\n3. Solicit feedback from a software engineer."
        },
        new()
        {
            Id = "pm-tut-02",
            Title = "Conducting High-Signal User Research Interviews (The Mom Test in Action)",
            Url = "https://www.youtube.com/watch?v=tY8mZ0Zp_z4",
            ThumbnailUrl = "https://img.youtube.com/vi/tY8mZ0Zp_z4/hqdefault.jpg",
            Source = "YouTube · Y Combinator",
            ChannelOrAuthor = "Y Combinator",
            Duration = "30 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A customer discovery interview script that uncovers genuine willingness to pay without biasing participants.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Eliminating Hypothetical Questions", Instruction = "Strip out 'Would you buy...' questions and focus 100% on past concrete behaviors." },
                new() { StepNumber = 2, Title = "Digging into Existing Workarounds", Instruction = "Ask how users currently solve the problem and how much money/time they spend on workarounds." },
                new() { StepNumber = 3, Title = "Extracting Emotional Stakes & Urgency", Instruction = "Identify why the problem matters and what happens if it remains unsolved." },
                new() { StepNumber = 4, Title = "Synthesizing Insights into Feature Prioritization", Instruction = "Cluster interview responses into problem patterns and pain point frequency matrices." }
            ],
            WhyItMatters = "Building features nobody wants is the #1 killer of startups; high-signal discovery ensures you build what people truly need.",
            YourEdge = "You extract honest market feedback that reveals real willingness to pay before writing a single line of code.",
            NextMove = "1. Write a 5-question user research script.\n2. Conduct 3 discovery interviews with real users.\n3. Document the top 2 repeated pain points."
        },
        new()
        {
            Id = "pm-dev-01",
            Title = "Product-Led AI: How AI Changes Product Management & Roadmapping",
            Url = "https://www.youtube.com/watch?v=FqG_6p7s8Z0",
            ThumbnailUrl = "https://img.youtube.com/vi/FqG_6p7s8Z0/hqdefault.jpg",
            Source = "YouTube · Reforge",
            ChannelOrAuthor = "Reforge",
            Duration = "28 min",
            Level = "Beginner",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhatHappened = "Reforge breaks down why AI features require probabilistic thinking, continuous evaluation datasets, and dynamic feedback loops rather than static wireframes.",
            WhyItMatters = "Deterministic features either work or crash; AI features work with variable quality, requiring PMs to master accuracy metrics and human-in-the-loop fallback design.",
            YourEdge = "You stand out from traditional PMs by knowing how to specify model benchmarks, evaluation datasets, and graceful degradation states.",
            NextMove = "1. Map out an AI feature in your product.\n2. Define what happens when model confidence is low (<70%).\n3. Design the fallback UI experience."
        }
    ];

    // ── Software Engineering & Web Development ───────────────────────────────

    private static List<LearningResource> SoftwareEngineeringResources() =>
    [
        new()
        {
            Id = "se-tut-01",
            Title = "Modern Web Architecture: Building a RESTful API with ASP.NET Core & MongoDB",
            Url = "https://www.youtube.com/watch?v=vV_X1pPjQ5E",
            ThumbnailUrl = "https://img.youtube.com/vi/vV_X1pPjQ5E/hqdefault.jpg",
            Source = "YouTube · Nick Chapsas",
            ChannelOrAuthor = "Nick Chapsas",
            Duration = "48 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A production-grade C# .NET Web API with clean repository architecture, MongoDB persistence, and OpenAPI documentation.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Clean Architecture Project Scaffolding", Instruction = "Create Controllers, Models, Services, and Data layers with dependency injection." },
                new() { StepNumber = 2, Title = "MongoDB Driver & Index Configuration", Instruction = "Connect to MongoDB Atlas, create compound indexes, and map POCO documents." },
                new() { StepNumber = 3, Title = "CRUD Controller Endpoints", Instruction = "Implement GET, POST, PUT, DELETE endpoints with model validation and proper HTTP status codes." },
                new() { StepNumber = 4, Title = "Error Handling & Logging Middleware", Instruction = "Implement global exception handling middleware with structured Serilog logging." }
            ],
            WhyItMatters = "Strong backend design guarantees that high-traffic web applications remain responsive, maintainable, and easy to scale.",
            YourEdge = "You write enterprise-grade, clean backend code that easily passes senior technical interviews and production code reviews.",
            NextMove = "1. Scaffold a new .NET 10 Web API project.\n2. Connect to local MongoDB.\n3. Implement a repository with indexed queries."
        },
        new()
        {
            Id = "se-tut-02",
            Title = "React 19 & TypeScript: Modern Frontend State Management & Hooks",
            Url = "https://www.youtube.com/watch?v=SqcY0GlETPk",
            ThumbnailUrl = "https://img.youtube.com/vi/SqcY0GlETPk/hqdefault.jpg",
            Source = "YouTube · Jack Herrington",
            ChannelOrAuthor = "Jack Herrington",
            Duration = "42 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A reactive TypeScript web interface featuring TanStack React Query data caching and optimistic UI updates.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Vite + React + TypeScript Setup", Instruction = "Initialize a lightweight project with strict TypeScript linting and path aliases." },
                new() { StepNumber = 2, Title = "Custom Hooks & Asynchronous State", Instruction = "Manage server data fetching, error boundaries, and loading skeletons cleanly." },
                new() { StepNumber = 3, Title = "TanStack Query Cache Invalidation", Instruction = "Implement useQuery and useMutation with automatic query invalidation and optimistic updates." },
                new() { StepNumber = 4, Title = "Responsive Component Styling", Instruction = "Build modular CSS with theme variables that adapt seamlessly from mobile to desktop." }
            ],
            WhyItMatters = "Fast, responsive web interfaces with instant optimistic state give digital products a snappy, polished, world-class feel.",
            YourEdge = "You build web apps that never flash blank screens during network requests and maintain clean TypeScript type safety from API to UI.",
            NextMove = "1. Build a React component consuming a paginated API.\n2. Implement TanStack Query caching.\n3. Add optimistic UI updates on button clicks."
        },
        new()
        {
            Id = "se-dev-01",
            Title = "System Design 2026: Microservices vs Modular Monoliths in High-Scale Systems",
            Url = "https://www.youtube.com/watch?v=z8XqQ8Y0l_s",
            ThumbnailUrl = "https://img.youtube.com/vi/z8XqQ8Y0l_s/hqdefault.jpg",
            Source = "YouTube · Hussein Nasser",
            ChannelOrAuthor = "Hussein Nasser",
            Duration = "32 min",
            Level = "Beginner",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhatHappened = "Critical analysis of why leading tech teams are pivoting back from complex microservice overhead to clean modular monoliths that deploy in single containers.",
            WhyItMatters = "Prevents early-stage startups and engineering teams from wasting months managing Kubernetes clusters when a fast monolith serves millions of users effortlessly.",
            YourEdge = "You make strategic architectural choices based on actual system throughput and developer velocity rather than cloud resume-driven development.",
            NextMove = "1. Read Martin Fowler's MonolithFirst essay.\n2. Audit your system's network boundaries.\n3. Evaluate where in-memory function calls beat network RPCs."
        }
    ];

    // ── Finance, Fintech & Venture ───────────────────────────────────────────

    private static List<LearningResource> FinanceAndFintechResources() =>
    [
        new()
        {
            Id = "fin-tut-01",
            Title = "Building a 3-Statement Financial Model & DCF Valuation from Scratch",
            Url = "https://www.youtube.com/watch?v=f_Vp_7q7k5w",
            ThumbnailUrl = "https://img.youtube.com/vi/f_Vp_7q7k5w/hqdefault.jpg",
            Source = "YouTube · RareLiquid",
            ChannelOrAuthor = "RareLiquid",
            Duration = "52 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "An integrated 3-statement financial forecast linking Income Statement, Balance Sheet, and Cash Flow Statement with scenario switches.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Historical Financial Ingestion", Instruction = "Input 3 years of historical revenue, COGS, OPEX, and capital expenditure data." },
                new() { StepNumber = 2, Title = "Working Capital & Depreciation Schedules", Instruction = "Build dynamic schedules for accounts receivable, accounts payable, and fixed asset depreciation." },
                new() { StepNumber = 3, Title = "Linking the 3 Statements Dynamically", Instruction = "Ensure net income flows into retained earnings and ending cash ties out across all statements." },
                new() { StepNumber = 4, Title = "Discounted Cash Flow (DCF) & Sensitivity Tables", Instruction = "Calculate WACC, project free cash flows to firm, and construct sensitivity data tables." }
            ],
            WhyItMatters = "Financial models are how capital allocators value businesses; mastering 3-statement modeling enables you to speak the language of institutional investors.",
            YourEdge = "You can stress-test any commercial proposition with dynamic sensitivity tables that reveal the exact breakeven unit economics.",
            NextMove = "1. Build an integrated 3-statement model for a SaaS business.\n2. Create Bull, Base, and Bear scenario toggles.\n3. Calculate net present value."
        },
        new()
        {
            Id = "fin-dev-01",
            Title = "The Future of African Cross-Border Payments: PAPSS, Stablecoins & Real-Time Rails",
            Url = "https://www.youtube.com/watch?v=7bX_q0Y8k6o",
            ThumbnailUrl = "https://img.youtube.com/vi/7bX_q0Y8k6o/hqdefault.jpg",
            Source = "YouTube · The Flip Africa",
            ChannelOrAuthor = "The Flip Africa",
            Duration = "30 min",
            Level = "Beginner",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhatHappened = "Investigation into how cross-border clearing networks (PAPSS) and digital stablecoin settlement are solving currency liquidity bottlenecks across the continent.",
            WhyItMatters = "Cross-border fees in Africa historically averaged 8-10%; next-generation payment rails are dropping transaction settlement times from days to seconds.",
            YourEdge = "You understand the regulatory and technical settlement infrastructure enabling the next wave of pan-African fintech and commerce platforms.",
            NextMove = "1. Review the PAPSS integration documentation.\n2. Compare settlement speed and foreign exchange fees across traditional banks vs digital rails."
        }
    ];

    // ── Entrepreneurship & Startups ──────────────────────────────────────────

    private static List<LearningResource> EntrepreneurshipResources() =>
    [
        new()
        {
            Id = "ent-tut-01",
            Title = "How to Build and Launch a Minimum Viable Product (MVP) in 2 Weeks",
            Url = "https://www.youtube.com/watch?v=1hHMwLxN6EM",
            ThumbnailUrl = "https://img.youtube.com/vi/1hHMwLxN6EM/hqdefault.jpg",
            Source = "YouTube · Y Combinator",
            ChannelOrAuthor = "Michael Seibel (Y Combinator)",
            Duration = "25 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A scoped MVP feature specification focused exclusively on delivering the single core transaction to early adopters.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Ruthless Scope Trimming", Instruction = "Cut every feature that is not required to complete the core user transaction." },
                new() { StepNumber = 2, Title = "Building with Off-the-Shelf Tools", Instruction = "Use existing auth, payments, and hosting services instead of writing custom plumbing." },
                new() { StepNumber = 3, Title = "Manual 'Concierge' Backends", Instruction = "Fulfill complex operational steps manually behind the scenes to validate demand before automating." },
                new() { StepNumber = 4, Title = "Direct User Feedback Loops", Instruction = "Embed live chat and feedback prompts directly in the user flow to talk to every single user." }
            ],
            WhyItMatters = "Founders fail by spending 6 months building in secret; launching an MVP in 2 weeks gets real market feedback before money runs out.",
            YourEdge = "You execute with extreme speed, validating hypotheses and securing paying users while competitors are still writing specifications.",
            NextMove = "1. List the 5 features you plan to build.\n2. Cut 4 of them.\n3. Build and launch the remaining 1 feature within 7 days."
        },
        new()
        {
            Id = "ent-dev-01",
            Title = "The New Rules of Venture Capital: Capital Efficiency, Unit Economics & Profitable Growth",
            Url = "https://www.youtube.com/watch?v=Qp_Z8q7x0Ys",
            ThumbnailUrl = "https://img.youtube.com/vi/Qp_Z8q7x0Ys/hqdefault.jpg",
            Source = "YouTube · All-In Podcast",
            ChannelOrAuthor = "All-In Podcast",
            Duration = "38 min",
            Level = "Beginner",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhatHappened = "Venture investors analyze the post-ZIRP environment where startups are funded based on positive gross margins and clear paths to cash flow profitability.",
            WhyItMatters = "Growth-at-all-costs is dead; startups that demonstrate capital efficiency and strong retention are capturing 90% of current venture funding.",
            YourEdge = "You structure venture propositions around sound unit economics from day one, making your business resilient and highly attractive to investors.",
            NextMove = "1. Calculate your Customer Acquisition Cost (CAC) and Lifetime Value (LTV).\n2. Verify LTV is at least 3x CAC.\n3. Target an under-12-month CAC payback period."
        }
    ];

    // ── Technology Policy & Ecosystems ───────────────────────────────────────

    private static List<LearningResource> TechnologyPolicyResources() =>
    [
        new()
        {
            Id = "pol-tut-01",
            Title = "How to Write a Tech Policy Brief & Regulatory Impact Analysis",
            Url = "https://www.youtube.com/watch?v=0k_X8q7y5Zs",
            ThumbnailUrl = "https://img.youtube.com/vi/0k_X8q7y5Zs/hqdefault.jpg",
            Source = "YouTube · Harvard Kennedy School",
            ChannelOrAuthor = "Harvard Belfer Center",
            Duration = "32 min",
            Level = "Beginner",
            Type = LearningResourceType.TutorialVideo,
            WhatYouWillBuild = "A structured 2-page policy memorandum evaluating regulatory sandboxes and consumer data privacy protections.",
            StepByStepBreakdown =
            [
                new() { StepNumber = 1, Title = "Executive Summary & Policy Problem", Instruction = "State the technological development and the specific regulatory dilemma in one paragraph." },
                new() { StepNumber = 2, Title = "Stakeholder Impact Assessment", Instruction = "Analyze the economic trade-offs for startups, incumbent institutions, and end-consumers." },
                new() { StepNumber = 3, Title = "Comparative Jurisdictional Analysis", Instruction = "Benchmark how Kenya, Nigeria, South Africa, and the UK have structured regulatory sandboxes." },
                new() { StepNumber = 4, Title = "Actionable Policy Recommendations", Instruction = "Provide 3 prioritized, phased regulatory actions that encourage innovation while protecting consumers." }
            ],
            WhyItMatters = "Technological innovation outpaces regulation; individuals who can bridge technical engineering with public policy command immense strategic influence.",
            YourEdge = "You provide government leaders and founders with pragmatic frameworks that balance consumer protection with economic velocity.",
            NextMove = "1. Select an emerging tech issue (e.g. AI copyright or stablecoin payments).\n2. Draft a 2-page brief.\n3. Identify 3 regulatory authorities affected."
        },
        new()
        {
            Id = "pol-dev-01",
            Title = "National AI Strategies Across Emerging Markets: Sovereignty, Compute & Data Governance",
            Url = "https://www.youtube.com/watch?v=k_X8z7y5Zs0",
            ThumbnailUrl = "https://img.youtube.com/vi/k_X8z7y5Zs0/hqdefault.jpg",
            Source = "YouTube · Center for Strategic and International Studies",
            ChannelOrAuthor = "CSIS",
            Duration = "40 min",
            Level = "Beginner",
            Type = LearningResourceType.NewDevelopmentVideo,
            WhatHappened = "Analysis of how emerging economies are structuring national AI data sovereignty policies, local compute subsidies, and indigenous language training datasets.",
            WhyItMatters = "Determines where sovereign data must reside and shapes public procurement contracts for technology providers over the next decade.",
            YourEdge = "You anticipate regulatory compliance requirements before they become mandatory law, positioning your products as safe, compliant market leaders.",
            NextMove = "1. Read Nigeria's National AI Strategy document.\n2. Identify data residency and compute infrastructure priorities."
        }
    ];
}
