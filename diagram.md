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

## 3. `/agents/foundryMemoryAgent` — Foundry Memory Store (Foundry-Native)

```mermaid
sequenceDiagram
    participant Client
    participant Route as POST /agents/foundryMemoryAgent
    participant Store as FoundryMemorySessionStore<br/>(in-memory, keyed by conversationId)
    participant Agent as AIAgent<br/>(memory-enabled Foundry agent)
    participant MemoryAPI as AIProjectMemoryStores<br/>(V2 protocol methods)
    participant Foundry as Azure AI Foundry

    Note over Store,Foundry: Startup (once): create/resolve memory store and memory-enabled agent

    Client->>Route: POST {message, userId, conversationId?}

    Note over Route: Set x-memory-user-id header from userId for long-term memory scope

    alt conversationId provided and found in session store
        Route->>Store: TryGet(conversationId)
        Store-->>Route: existing AgentSession
        Note over Route: Reuse existing Foundry session (short-term memory continuity)
    else conversationId missing or not found
        Route->>Agent: CreateSessionAsync()
        Agent->>Foundry: Create new session/thread
        Foundry-->>Agent: session
        Route->>Store: Set(newConversationId, session)
        Note over Route: New conversationId generated and returned
    end

    Route->>Agent: RunAsync(message, agentSession)
    Agent->>Foundry: Execute via Foundry agent
    Foundry->>MemoryAPI: Retrieve/update user-scoped memory (managed by Foundry)
    Foundry-->>Agent: Response
    Agent-->>Route: AgentResponse

    Route-->>Client: 200 OK {userId, conversationId, response}

    Note over Store: Session store survives process lifetime only
    Note over MemoryAPI: Foundry memory store persists long-term user context across restarts
```