# MCP Security: Summary of Key Findings and Best Practices

## The State of MCP Security in 2026

MCP is on track to become the de facto standard for AI tool integration, with adoption accelerating across major vendors and the open‑source ecosystem. The security posture has not kept pace with that adoption.

Through early 2026, researchers disclosed over 30 CVEs against MCP implementations across Python, TypeScript, Java, and Rust SDKs — including three in Anthropic's own reference Git server on a single day (January 20, 2026). Pynt's analysis of 281 real MCP implementations found a single server carries a 9% exploitation risk, compounding to over 50% at three servers and 92% at ten. Endor Labs' analysis of 2,614 implementations found 82% use file operations prone to path traversal, 67% use APIs related to code injection, and 34% use APIs susceptible to command injection.

The official MCP specification acknowledges this directly:

> *"While MCP itself cannot enforce these security principles at the protocol level, implementors SHOULD..."*

Everything in the security model is advisory. There are no enforced defaults.

---

## The Core Problem

MCP breaks the traditional API security model. Conventional API security — gateways, WAFs, schema validation, rate limiting — assumes a bounded, synchronous request/response between two systems. MCP introduces a third actor: the LLM. The LLM treats tool descriptions and tool results as semantic content it reasons about, not bytes it validates. This creates an entirely new threat model that traditional security primitives were not designed to address.

The attack surface is semantic, not syntactic. You can validate a JWT. You cannot fully validate whether a tool description contains hidden adversarial instructions, because the "vulnerability" lives in how a probabilistic model interprets natural language.

---

## The Attack Surface — Five Layers

### 1. Prompt Injection via Tool Results
The most common and dangerous attack class. An agent calls a tool that fetches external content — a web page, an email, a document — and returns it as the tool result. That content contains adversarial instructions. Because the agent processes tool results as trusted context, it complies. The agent cannot distinguish between "data I fetched" and "instruction I received." This is not resolved by containerization — containers stop lateral movement after execution, not semantic manipulation of the model.

### 2. Tool Poisoning (Supply Chain)
MCP tool descriptions enter the agent's context window as trusted content. An attacker who controls a tool description — through a malicious or compromised third-party server — can embed hidden instructions the LLM reads and acts on. The official spec acknowledges this: *"descriptions of tool behavior such as annotations should be considered untrusted, unless obtained from a trusted server."*

### 3. Authentication Gaps
Many MCP servers ship with no authentication or trivially bypassable auth. A representative example: CVE-2026-32211, disclosed April 3, 2026, is a critical authentication flaw in Microsoft's Azure MCP Server carrying a CVSS score of 9.1. Missing authentication for a critical function allows an unauthorized attacker to disclose information — including configuration details, API keys, and authentication tokens — over the network. As of late May 2026 it remains unpatched, with only mitigation guidance available. A separate survey of 5,200 open-source MCP servers found 53% rely on long-lived static secrets, with OAuth adoption at only 8.5%.

### 4. Path Traversal and Injection
82% of analyzed implementations use file operations prone to path traversal. 67% use APIs related to code injection. 34% use APIs susceptible to command injection. These are classic vulnerabilities now embedded inside AI agent infrastructure, where the impact radius is significantly larger.

### 5. Privilege Escalation via Chaining
In multi-server agent architectures where the agent is configured to trust tool results autonomously, a compromised tool with limited initial access can pivot across connected services. This is architectural risk, not inherent to the protocol — it depends on how many servers are connected and how much autonomy the agent has. The compounding probability data makes this concrete: at ten connected servers, Pynt measured a 92% exploitation probability.

---

## What the Official Spec Says

The `2025-11-25` specification — the current stable release — is honest about its limitations. Key points:

- The protocol cannot enforce its own security principles at the protocol level
- Tool descriptions should be considered untrusted unless from a trusted server
- Authorization is **optional** — present in the spec as OAuth 2.1 + PKCE, but not enforced by the protocol
- Security guidance exists but is advisory, not prescriptive

The security documentation covers Confused Deputy attacks, Token Passthrough, SSRF during OAuth metadata discovery, Session Hijacking, and Local Server Compromise. The knowledge is there. The defaults are not.

**Important clarification on local server risk:** MCP servers are arbitrary programs and can execute arbitrary code — that is the risk. The protocol does not define a "startup config" mechanism that executes code; rather, MCP clients can be configured to launch server processes, including malicious ones embedded in client configuration. The vulnerability is at the client configuration layer, not the protocol layer.

---

## The Historical Parallel

This follows a well-documented pattern:

| Era | Default | Result |
|---|---|---|
| Early internet | Ship it, security later | Decades of pain |
| npm ecosystem | Publish everything, no vetting | Ongoing supply chain attacks |
| Early S3 | Buckets public by default | Thousands of breaches before AWS changed the default in 2023 |
| Early mobile | Apps requesting every permission | Years of adware before platform controls tightened |
| MCP 2025–2026 | Auth optional, no signing requirement, no enforced sandboxing | 30+ CVEs in the first year, including in Anthropic's own reference implementation |

What makes MCP more dangerous than past cycles: the harm is active, not passive. A public S3 bucket leaks data passively. A compromised MCP server attached to an autonomous agent can exfiltrate, pivot, and cause damage at machine speed with no human in the loop.

The forcing function will be enterprise procurement and liability — not vendor conscience. When a breach is clearly attributable to an MCP integration, legal and compliance pressure flows upstream faster than any security research.

---

## Best Practices and Defensive Architecture

### The Core Posture: Build and Own Your Servers

**Only connect to MCP servers you built and control.** The only way to have a trusted server, as the spec defines it, is to be the one who built and operates it. Third-party MCP servers introduce every layer of the attack surface above, with no audit trail you control.

This requires open source as a minimum bar for any third-party server you evaluate. Any vendor shipping a closed-source MCP server is asking you to trust a black box executing in your environment with access to your agent's context. Even then, open source is a floor, not a guarantee.

### The Docker Image Pattern

Treat MCP servers like Docker images from untrusted registries:

- Pull the Dockerfile, never run an image directly from a public registry
- Audit the source code before building
- Build the image yourself
- Store it in your private registry
- Pin by digest, not by tag
- Control the base image and all dependencies
- Maintain an SBOM for every server including upstream dependencies

### Container + stdio Architecture

Run all MCP servers in containers using stdio transport:

**What the container boundary provides:**
- Tool calls can only touch explicitly mounted filesystem paths — path traversal hits the container wall, not the host
- No network egress unless explicitly opened — a compromised server cannot phone home or pivot to your internal network
- Process isolation — the server cannot inspect other processes, credentials, or environment variables outside the container
- Bounded blast radius — worst case is a destroyed container, not a compromised host

**What stdio provides over HTTP/SSE:**
- No open port for external actors to probe
- Server process lives only as long as the agent session — no persistent attack surface
- No auth layer to misconfigure because there is no network surface to authenticate against

**One container per MCP server.** A compromised server cannot touch other servers. Treat inter-server trust the same way microservices treat inter-service trust — zero by default.

Mount volumes read-only unless the tool explicitly requires write access.

**What this does not solve:** Prompt injection via tool results is fully alive inside a container. Treat all tool results as untrusted input — the same way you treat user input from the internet — before the model acts on them.

### Thin, Scoped Wrappers Over Third-Party APIs

Instead of connecting a broad third-party MCP server, build a thin wrapper exposing exactly the endpoints your workflow needs:

- Define explicit tool schemas for each operation
- Authenticate via environment variables, never hardcoded credentials
- Separate read tools from write tools
- Require explicit confirmation flows for any operation with side effects
- Scope to the minimum necessary permissions

Tools like Claude Code can generate a production-quality thin MCP wrapper in a single session given a clear specification of which API endpoints to expose and what constraints to enforce.

### Pre-Deploy Checklist

- Pin every MCP server version in source control
- Block CI on unpinned servers
- Run static analysis against tool definitions on every config change
- Require code review for any new server added to an agent's allowed list
- Maintain an SBOM including transitive dependencies
- Subscribe to the Vulnerable MCP Project and GitHub Security Advisories for the MCP ecosystem

### Runtime Controls

- Scan tool descriptions on every session — not just first approval
- Scan tool arguments for credential patterns and encoded payloads
- Never auto-approve tool calls for operations with side effects
- Implement token budgets as circuit breakers for runaway agent loops
- Log all tool calls with correlation IDs for audit trail

### Authentication (for HTTP/SSE Transport)

If you must run remote MCP servers, implement OAuth 2.1 + PKCE with:
- Audience validation — tokens must contain an `aud` claim matching the specific server URI
- Short-lived access tokens
- Token introspection on every request
- Per-client consent tracking (not shared consent cookies)
- Least-privilege scopes — never wildcard or omnibus grants
- Exact redirect URI matching, no wildcards
- HTTPS enforced in production, never plain HTTP

---

## The Bottom Line

The engineers and teams who establish disciplined MCP security practices now — before it's mandated by compliance frameworks or forced by a significant breach — will have a substantial advantage. The infrastructure investment is modest: containerized stdio servers, private registries, thin scoped wrappers, and audit tooling. The security posture it creates is significantly stronger than what most enterprises will have for the next two years.

Treat every MCP server like infrastructure, not a dependency. Build it, audit it, own it, run it in a container, and treat everything it returns as untrusted input.
