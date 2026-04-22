# Agent Hub — Sequence Diagrams

## 1. `/agents/demo` — Direct Model Inference with PostgreSQL Memory

```mermaid
sequenceDiagram
    participant Client
    participant Route as POST /agents/demo
    participant SessionMgr as IConversationSessionManager<br/>(in-memory + PostgreSQL)
    participant Agent as AIAgent<br/>(direct model inference)
    participant Postgres as PostgreSQL
    participant Foundry as Azure AI Foundry (model endpoint)

    Note over SessionMgr,Postgres: Startup (once): Agent created inline via AIProjectClient.AsAIAgent(model,instructions)

    Client->>Route: POST {message, conversationId?}

    alt existing conversationId with cached session
        Route->>SessionMgr: GetOrCreateSessionAsync(conversationId)
        SessionMgr-->>Route: ConversationSessionContext (session reused)
        Note over SessionMgr: Cache HIT — no history replay needed
    else new conversationId or session evicted
        Route->>SessionMgr: GetOrCreateSessionAsync(conversationId)
        SessionMgr->>Postgres: Load conversation history
        Postgres-->>SessionMgr: prior turns (user + assistant messages)
        SessionMgr->>Agent: CreateSessionAsync()
        Agent->>Foundry: Create new session
        Foundry-->>Agent: session
        SessionMgr-->>Route: ConversationSessionContext (requiresHistoryReplay=true)
        Note over SessionMgr: Cache MISS — history replayed into session
    end

    alt requiresHistoryReplay
        Route->>Agent: RunAsync(historyMessages + message, session)
    else
        Route->>Agent: RunAsync(message, session)
    end

    Agent->>Foundry: Inference request
    Foundry-->>Agent: Response
    Agent-->>Route: AgentResponse

    Route->>SessionMgr: AppendTurnAsync(conversationId, message, response)
    SessionMgr->>Postgres: Persist user + assistant turn

    Route-->>Client: 200 OK {conversationId, response}
```

---

## 2. `/agents/foundry-demo` — Foundry-Managed Agent with PostgreSQL Memory

```mermaid
sequenceDiagram
    participant Client
    participant Route as POST /agents/foundry-demo
    participant SessionMgr as IConversationSessionManager<br/>(in-memory + PostgreSQL)
    participant Agent as FoundryAgent<br/>(declarative agent on Foundry)
    participant AgentAdmin as AgentAdministrationClient
    participant Postgres as PostgreSQL
    participant Foundry as Azure AI Foundry

    Note over AgentAdmin,Foundry: Startup (once): resolve or create Foundry agent by name via AgentAdministrationClient

    alt agent exists in Foundry
        AgentAdmin->>Foundry: GetAgentAsync(agentName)
        Foundry-->>AgentAdmin: ProjectsAgentRecord
    else agent does not exist
        AgentAdmin->>Foundry: CreateAgentVersionAsync(agentName, definition)
        Foundry-->>AgentAdmin: created
        AgentAdmin->>Foundry: GetAgentAsync(agentName)
        Foundry-->>AgentAdmin: ProjectsAgentRecord
    end

    Client->>Route: POST {message, conversationId?}

    alt existing conversationId with cached session
        Route->>SessionMgr: GetOrCreateSessionAsync(conversationId)
        SessionMgr-->>Route: ConversationSessionContext (session reused)
        Note over SessionMgr: Cache HIT — no history replay needed
    else new conversationId or session evicted
        Route->>SessionMgr: GetOrCreateSessionAsync(conversationId)
        SessionMgr->>Postgres: Load conversation history
        Postgres-->>SessionMgr: prior turns
        SessionMgr->>Agent: CreateSessionAsync()
        Agent->>Foundry: Create new session/thread
        Foundry-->>Agent: session
        SessionMgr-->>Route: ConversationSessionContext (requiresHistoryReplay=true)
        Note over SessionMgr: Cache MISS — history replayed into session
    end

    alt requiresHistoryReplay
        Route->>Agent: RunAsync(historyMessages + message, session)
    else
        Route->>Agent: RunAsync(message, session)
    end

    Agent->>Foundry: Execute via declarative agent
    Foundry-->>Agent: Response
    Agent-->>Route: AgentResponse

    Route->>SessionMgr: AppendTurnAsync(conversationId, message, response)
    SessionMgr->>Postgres: Persist user + assistant turn

    Route-->>Client: 200 OK {conversationId, response}
```

---

## 3. `/agents/foundryMemoryAgent` — Foundry Memory Store with Local Topic Detection

```mermaid
sequenceDiagram
    participant Client
    participant Route as POST /agents/foundryMemoryAgent
    participant EmbedSvc as LocalEmbeddingService<br/>(ONNX inference, ~5-10ms)
    participant TopicChk as TopicRelevanceChecker<br/>(embedding + TF-IDF fallback)
    participant Cache as FoundryMemorySessionCache<br/>(sessions + local turn buffer, keyed by userId)
    participant OpCache as FoundryMemoryOperationCache<br/>(tracks search/update IDs)
    participant Agent as AIAgent<br/>(memory-enabled Foundry agent)
    participant MemoryAPI as AIProjectMemoryStores<br/>(V2 protocol methods)
    participant Foundry as Azure AI Foundry

    Note over Cache,Foundry: Startup (once): create/resolve memory store, memory agent, session cache, and operation cache

    Client->>Route: POST {message, userId}

    Note over Route,TopicChk: Compute local embedding for topic detection (no network call)
    Route->>EmbedSvc: Embed(message)
    alt ONNX model available
        EmbedSvc-->>Route: normalized float[] embedding (~5-10ms in-process)
    else model not available
        EmbedSvc-->>Route: null (will use TF-IDF fallback)
    end

    alt userId has a cached session (returning user)
        Route->>Cache: GetOrCreateSessionAsync(userId)
        Cache-->>Route: (existing AgentSession, isNew=false)
        Note over Cache: Cache HIT

        Route->>Cache: GetTurns(userId)
        Cache-->>Route: last 5 turns (local buffer)
        
        Note over Route,TopicChk: Check if message is on-topic using local embedding + TF-IDF fallback
        Route->>TopicChk: IsOnTopic(message, cachedTurns, queryEmbedding)
        TopicChk-->>Route: isOnTopic (true/false)
        
        alt isOnTopic (message is related to recent turns)
            Note over Route: Fast path — use only local turn history
            Note over Route: Build context: [cached turns as user/assistant messages] + current message
        else topic shift detected
            Note over Route: Slow path — refresh memory from Foundry
            Route->>OpCache: GetPreviousSearchId(userId)
            OpCache-->>Route: previousSearchId (or null)
            Route->>MemoryAPI: SearchMemoriesAsync(storeName, V2 protocol request)
            Note over MemoryAPI: V2 JSON: {scope, items: [{type: "message", role: "user", content: message}], previous_search_id, options}
            MemoryAPI->>Foundry: Search memories (embedding via text-embedding-3-small)
            Foundry-->>MemoryAPI: MemoryStoreSearchResponse (memories + searchId)
            MemoryAPI-->>Route: matched memories
            Route->>OpCache: RememberSearchId(userId, searchId)
            Note over Route: Build context: [Foundry memory] + [cached turns] + current message
        end
    else no session for this userId (first request or after restart)
        Route->>Cache: GetOrCreateSessionAsync(userId)
        Cache->>Agent: CreateSessionAsync()
        Agent->>Foundry: Create new session/thread
        Foundry-->>Agent: session
        Agent-->>Cache: new AgentSession
        Cache-->>Route: (new AgentSession, isNew=true)
        Note over Cache: Cache MISS — search Foundry for bootstrap context (first request)

        Route->>OpCache: GetPreviousSearchId(userId)
        OpCache-->>Route: previousSearchId (or null)
        Route->>MemoryAPI: SearchMemoriesAsync(storeName, V2 protocol request)
        Note over MemoryAPI: V2 JSON: {scope, items: [{type: "message", role: "user", content: message}], previous_search_id, options}
        MemoryAPI->>Foundry: Search memories (embedding via text-embedding-3-small)
        Foundry-->>MemoryAPI: MemoryStoreSearchResponse (memories + searchId)
        MemoryAPI-->>Route: matched memories
        Route->>OpCache: RememberSearchId(userId, searchId)
        Note over Route: Build context: [system: memory context] + current message
    end

    Note over Route,Agent: Run agent with context messages
    Route->>Agent: RunAsync(contextMessages, agentSession)
    Agent->>Foundry: Execute via Foundry agent
    Foundry-->>Agent: Response
    Agent-->>Route: AgentResponse

    Note over Route,Cache: Cache the turn locally with embedding (bounded to last 20)
    Route->>Cache: AppendTurn(userId, message, response, queryEmbedding)

    Route-->>Client: 200 OK {userId, response}

    Note over Route,MemoryAPI: Fire-and-forget: persist to Foundry memory (non-blocking)
    Route-)MemoryAPI: UpdateMemoriesAsync(storeName, V2 protocol request)
    Note over MemoryAPI: V2 JSON: {scope, items: [{type: "message", role: "user", ...}, {type: "message", role: "assistant", ...}], previous_update_id, update_delay}
    MemoryAPI->>Foundry: Update memories (user profile + chat summary)
    Foundry-->>MemoryAPI: MemoryUpdateResult (updateId + status)
    MemoryAPI-->>OpCache: RememberUpdateId(userId, updateId)

    Note over Cache: Session + local turns remain cached with embeddings (survives future requests within process)
    Note over MemoryAPI: Long-term user context persisted in Azure (survives app restart)
```