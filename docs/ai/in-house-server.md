# Moving to the in-house AI server

Goal: no LT data leaves the company. The app needs no code change: point the jobs at the in-house server in Settings > AI connections.

## What the server must offer

LT ODM talks to in-house servers with the **OpenAI-compatible API**, which vLLM, Ollama, LM Studio and LocalAI all provide.

| App job | Endpoint used | Requirement |
|---|---|---|
| Test connection | `GET /v1/models` | Lists model ids |
| Text | `POST /v1/chat/completions` | **Structured output**: honours `response_format: {"type":"json_schema", ...}`. vLLM (guided decoding), Ollama 0.5+ and LM Studio do; without it answers may not parse and the app shows "could not read the answer" |
| Image | `POST /v1/images/generations` | Returns `b64_json`. LocalAI (diffusers backend with Stable Diffusion XL or FLUX) or any server with this endpoint. The sketch is **not** sent to in-house image servers (prompt only) |
| Vision (image search) | `POST /v1/chat/completions` | A vision model that accepts `image_url` content with a `data:` URL, plus structured output (Ollama, vLLM, LM Studio) |
| Embedding (image search) | `POST /v1/embeddings` | An embedding model whose vectors have exactly **1024 numbers** (`qwen3-embedding:0.6b`, `bge-m3`, `mxbai-embed-large`); larger ones such as `qwen3-embedding:4b` (2560) are refused |

The answer can include a reasoning `<think>…</think>` block or a ``` fence; the app strips both.

## Model guidance (approximate)

| Job | Suggested open models | Licence (commercial use) | Rough memory |
|---|---|---|---|
| Text (JSON answers, short explanations) | Qwen3 32B or Llama 3.3 70B instruct for best quality; Qwen3 14B for a smaller server; Qwen2.5 7B on CPU | Qwen3, Qwen2.5 7B: Apache 2.0. Llama 3.3: Llama Community License (allowed, with Meta's use policy) | 32B at 4-bit ≈ 20-24 GB; 70B at 4-bit ≈ 40-48 GB; 14B ≈ 10-12 GB; 7B ≈ 6 GB RAM |
| Image | FLUX.1-schnell or SDXL | FLUX.1-schnell: Apache 2.0. SDXL: Open RAIL++-M (allowed, with use restrictions). **Not FLUX.1-dev** (non-commercial licence) | 12-24 GB |
| Vision (image search) | Qwen2.5-VL 7B (GPU or CPU); Gemma 3 4B | Qwen2.5-VL 7B: Apache 2.0. Gemma 3: Gemma Terms of Use (allowed, with Google's use restrictions). **Not Qwen2.5-VL 3B** (Qwen Research License: non-commercial) | 7B ≈ 8 GB RAM on CPU, about 1-2 minutes per picture; Gemma 3 4B ≈ 4 GB |
| Embedding (image search) | Qwen3-Embedding 0.6B or BGE-M3 (both 1024 numbers, multilingual) | Qwen3-Embedding: Apache 2.0. BGE-M3: MIT | ≈ 1 GB; fine on CPU |
| Later (AI Lab): Document | Qwen2.5-VL 7B (tech packs) | Apache 2.0 | 8-24 GB |

**Check the licence before pulling any model.** Within one family the licence can change with the size (Qwen2.5 and Qwen2.5-VL: 7B Apache 2.0, but 3B research-only and 72B under the Qwen licence). The licence is on the model's page at ollama.com (the *License* section) and on its Hugging Face page. LT ODM is commercial use, so research-only and non-commercial models are not allowed, even on a dev server.

Test with real LT data before switching: the material reader and the BOM check explanations are the most demanding (long structured answers). Raise the connection's timeout (default 120 s) if a smaller server is slow.

> **Image search by photo needs no AI server.** Its look-alike model runs inside the LT ODM API ([File storage](../installation/08-file-storage.md#image-search-model-files)). The AI server is only needed for the descriptions behind search by words, and for the other AI features.

## Setting up Ollama (dev server, no GPU)

Ollama is the simplest in-house server to start with: one install, models pulled by name, and the OpenAI-compatible API at `http://<server>:11434/v1`. Without a GPU it runs on the CPU: fine for image search and testing, slow for long text answers. Plan **16 GB RAM** or more (the three models below together use about 15 GB while loaded) and 20 GB disk.

**Install (Ubuntu Server, recommended for a server):**

```bash
curl -fsSL https://ollama.com/install.sh | sh        # installs the ollama service (systemd)
sudo systemctl edit ollama                            # add the three lines below, save
#   [Service]
#   Environment="OLLAMA_HOST=0.0.0.0:11434"
#   Environment="OLLAMA_KEEP_ALIVE=30m"
sudo systemctl restart ollama
```

**Install (Windows Server):** run `OllamaSetup.exe` from ollama.com/download, set the system environment variables `OLLAMA_HOST=0.0.0.0:11434` and `OLLAMA_KEEP_ALIVE=30m`, then restart Ollama. The Windows app runs in the signed-in user's session, not as a service, and updates itself over the internet: prefer Linux for a server that must stay offline.

**Pull the models** (on the AI server):

```bash
ollama pull qwen2.5vl:7b            # Vision: reads garment pictures (Apache 2.0; not the 3b: research-only licence)
ollama pull qwen3-embedding:0.6b    # Embedding: 1024 numbers per text, Apache 2.0 (or: ollama pull bge-m3, MIT)
ollama pull qwen2.5:7b              # Text (optional for image search): Apache 2.0, no reasoning step so quicker on CPU
ollama list
```

**Keep it inside LT.**

- Ollama has **no sign-in**. Allow port 11434 from the **web server only** at the firewall; nobody else needs to reach it (browsers never do).
- Only `ollama pull` talks to the internet (`registry.ollama.ai`). Once the models are pulled, block the AI server's outbound internet access. For a server that never goes online, pull on another machine and copy the models folder (`/usr/share/ollama/.ollama/models` on Linux, `%USERPROFILE%\.ollama\models` on Windows).
- Ollama sends no prompts, pictures or usage data anywhere.

**Test from the web server** before touching the app (same calls the app makes; exit code 0 = all passed):

```powershell
.\tools\test-ai-server.ps1 -Endpoint http://ai-server.lt.local:11434/v1
# other models: -TextModel ... -VisionModel ... -EmbeddingModel ...; your own picture: -Picture D:\samples\jacket.jpg
```

It checks the model list, a JSON text answer, a picture reading and that the embedding model returns 1024 numbers, with the time each took. The first call to each model also loads it into memory.

**Point the jobs at it** (Settings > AI connections): add an OpenAI-compatible connection with address `http://ai-server.lt.local:11434/v1`, **Runs on the LT network** ticked and timeout **300** seconds or more (a CPU server needs it), then set **Vision** = `qwen2.5vl:7b` and **Embedding** = `qwen3-embedding:0.6b` on it. Then AI Studio > Image search > **Index pictures**.

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
