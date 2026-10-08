# Moving to the in-house AI server

Goal: no LT data leaves the company. The app needs no code change: point the jobs at the in-house server in Settings > AI connections.

## What the server must offer

LT ODM talks to in-house servers with the **OpenAI-compatible API**, which vLLM, Ollama, LM Studio and LocalAI all provide.

| App job | Endpoint used | Requirement |
|---|---|---|
| Test connection | `GET /v1/models` | Lists model ids |
| Text | `POST /v1/chat/completions` | **Structured output**: honours `response_format: {"type":"json_schema", ...}`. vLLM (guided decoding), Ollama 0.5+ and LM Studio do; without it answers may not parse and the app shows "could not read the answer" |
| Image | `POST /v1/images/generations` | Returns `b64_json`. LocalAI (diffusers backend with Stable Diffusion XL or FLUX) or any server with this endpoint. The sketch is **not** sent to in-house image servers (prompt only) |

The answer can include a reasoning `<think>…</think>` block or a ``` fence; the app strips both.

## Model guidance (approximate)

| Job | Suggested open models | Rough GPU memory |
|---|---|---|
| Text (JSON answers, short explanations) | Qwen3 32B or Llama 3.3 70B instruct for best quality; Qwen3 14B for a smaller server | 32B at 4-bit ≈ 20-24 GB; 70B at 4-bit ≈ 40-48 GB; 14B ≈ 10-12 GB |
| Image | FLUX.1-schnell / FLUX.1-dev, or SDXL | 12-24 GB |
| Later (AI Lab): Embedding, Vision, Document | e.g. SigLIP / CLIP (look-alike search, with SQL Server 2025 `VECTOR`), Qwen2.5-VL (tech packs, colour reading) | 8-24 GB each |

Test with real LT data before switching: the material reader and the BOM check explanations are the most demanding (long structured answers). Raise the connection's timeout (default 120 s) if a smaller server is slow.

## Switching over

1. **Add the connection.** Settings > AI connections > Add connection:
   - Type **OpenAI-compatible**
   - Address with the version, e.g. `http://ai-server.lt.local:8000/v1` (vLLM) or `http://ai-server.lt.local:11434/v1` (Ollama)
   - API key only if the server requires one
   - Tick **Runs on the LT network**
   - **Test connection**: it should list the models.
2. **Point the jobs.** Text → the new connection and its text model (pick from the list with the chip button); Image → the image connection and model. Save each row.
3. **Check.** Open AI Studio: the banner turns green ("In-house: data stays at LT"). Try smart search, a change summary, a render, Concept Studio's draft, and a material reading.
4. **Remove the cloud path.**
   - Set the appsettings fallbacks to in-house too (`Ai__Provider=OpenAiCompatible`, `Ai__ImageProvider=OpenAiCompatible`, `Ai__OpenAiCompatible__Endpoint`, `__Model`, `__ImageModel`), or remove `Ai__Gemini__ApiKey`, so a job reset to "App default" cannot fall back to Gemini.
   - Delete the Gemini connection in Settings > AI connections (only possible once no job uses it).
   - Turn **Allow cloud AI services** off (Settings > AI connections > Switches): even a job pointed back at Gemini by mistake then sends nothing.
   - Block `generativelanguage.googleapis.com` for the web server at the firewall.
5. **Firewall.** Allow the web server to reach the AI server's port (only the web server; browsers never talk to the AI server).

## What changes for users

- Nothing in the screens except the banner colour.
- Answer quality and speed depend on the models chosen; renders lose the sketch guidance.
- Concept Studio, Cost Optimization and Market Trends, which send the most sensitive data (prices and costs), are the first to benefit.
