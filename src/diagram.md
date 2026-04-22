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

## 3. `/agents/foundryMemoryAgent` — Foundry Memory Store with Session Cache

```mermaid
sequenceDiagram
    participant Client
    participant Route as POST /agents/foundryMemoryAgent
    participant Cache as FoundryMemorySessionCache<br/>(in-memory, keyed by userId)
    participant Agent as AIAgent<br/>(memory-enabled Foundry agent)
    participant MemoryStore as AIProjectMemoryStores<br/>(Foundry memory store)
    participant Foundry as Azure AI Foundry

    Note over Cache,Foundry: Startup (once): create/resolve memory store, memory agent, and session cache

    Client->>Route: POST {message, userId}

    alt userId has a cached session
        Route->>Cache: GetOrCreateSessionAsync(userId)
        Cache-->>Route: existing AgentSession (reused)
        Note over Cache: Cache HIT — conversation thread continues
    else no session for this userId
        Route->>Cache: GetOrCreateSessionAsync(userId)
        Cache->>Agent: CreateSessionAsync()
        Agent->>Foundry: Create new session/thread
        Foundry-->>Agent: session
        Agent-->>Cache: new AgentSession
        Cache-->>Route: new AgentSession (cached for future requests)
        Note over Cache: Cache MISS — new session created and cached
    end

    Route->>Agent: RunAsync(message, agentSession)
    Agent->>Foundry: Execute with memory-enabled agent
    Note over MemoryStore,Foundry: Foundry reads + writes user context via memory store (scoped by userId)
    Foundry-->>Agent: Reply
    Agent-->>Route: AgentResponse
    Route-->>Client: 200 OK {userId, response}

    Note over Cache: Session remains cached for userId (survives future requests)
    Note over MemoryStore: Long-term user context persisted in Azure (survives app restart)
```