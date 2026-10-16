# Moving to the in-house AI server

Goal: no LT data leaves the company. The app needs no code change: point the jobs at the in-house server in Settings > AI connections.

## What the server must offer

LT ODM talks to in-house servers with the **OpenAI-compatible API**, which vLLM, Ollama, LM Studio and LocalAI all provide.

| App job | Endpoint used | Requirement |
|---|---|---|
| Test connection | `GET /v1/models` | Lists model ids |
| Text | `POST /v1/chat/completions` | **Structured output**: honours `response_format: {"type":"json_schema", ...}`. vLLM (guided decoding), Ollama 0.5+ and LM Studio do; without it answers may not parse and the app shows "could not read the answer" |
| Image | `POST /v1/images/generations`; with a sketch `POST /v1/images/edits` | Returns `b64_json`. OpenVINO Model Server (FLUX.1-schnell), LocalAI (diffusers backend) or any server with these endpoints. A render with **Use the sketch** goes to `images/edits` (image-to-image, see [OpenVINO image model](#openvino-image-model-renders)); a server without it needs `Ai:OpenAiCompatible:ImageSketchStrength` = 0 (prompt only) |
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

**Without a GPU, text answers are slow.** On the tested laptop (MX550, 2 GB: qwen2.5:7b runs on the CPU) a Library assistant question takes minutes; on a GPU server with 16 GB or more it takes seconds. Ollama's default context window is 4096 tokens; set `OLLAMA_CONTEXT_LENGTH=8192` on the AI server if long prompts (many product types, more assistant tools) get cut.

Test with real LT data before switching: the material reader and the BOM check explanations are the most demanding (long structured answers). Raise the connection's timeout (default 120 s) if a smaller server is slow.

> **Image search needs no AI server.** Search by photo, by words and "more like this" use the image model built into the LT ODM API ([File storage](../installation/08-file-storage.md#image-search-model-files)). The AI server is needed for the other AI features (smart search's AI step, summaries, BOM check notes, Concept Studio, material reader...) and for image search with *Use the AI server* ticked.

## Choosing the server software

Any server with an OpenAI-style API works; nothing in the app changes. For a Windows server with an Intel CPU and no GPU (such as LTODMSVR):

| Server | When |
|---|---|
| **OpenVINO Model Server** (Intel, Apache 2.0) | **Recommended for Intel CPUs.** Runs as a Windows service; uses the CPU's AVX-512 / VNNI instructions with int4/int8 models. Steps below |
| Ollama (MIT) | Easiest to try, e.g. on a laptop. On Windows it runs in the signed-in user's session (not a service) and updates itself; on Linux it is a proper service. Steps further below |
| llama.cpp `llama-server` (MIT) | Simple fallback: one model per process, run as a service with a wrapper (NSSM / WinSW) |
| vLLM | Only with an NVIDIA GPU (Linux) |

Whatever is chosen, check it with `tools\test-ai-server.ps1` before pointing the app at it.

## Setting up OpenVINO Model Server (Windows, Intel CPU)

Tested with release **2026.4.1** on an Intel laptop CPU (October 2026). Two folders end up on the server: the program (`C:\ovms` below) and the models (`C:\models`, with `config.json`). Any folders work, e.g. `E:\LTODM\ai-server\ovms-2026.4.1\ovms` and `E:\LTODM\ai-server\models`: use them in step 3 (the service runs `ovms.exe` from the installer's own folder and reads `config.json` from the folder given to it). The model paths inside `config.json` are relative, so the models folder can be copied anywhere.

**Models** (all Apache 2.0, ready-made OpenVINO versions from huggingface.co/OpenVINO; the same models as the Ollama setup):

| App job | Model on the server | Name in the app | Size |
|---|---|---|---|
| Text | `OpenVINO/Qwen2.5-7B-Instruct-int4-ov` | `qwen2.5-7b` | about 4.5 GB |
| Vision | `OpenVINO/Qwen2.5-VL-7B-Instruct-int4-ov` | `qwen2.5-vl-7b` | about 5.5 GB |
| Embedding | `OpenVINO/Qwen3-Embedding-0.6B-int8-ov` (1024 numbers; needs `--pooling LAST`) | `qwen3-embedding-0.6b` | 0.6 GB |

**Measured** (laptop, Intel i7-1270P, CPU only, 2026-10-13; same requests as the Ollama check on that laptop):

| | OpenVINO Model Server | Ollama (same models) |
|---|---|---|
| Text: one short JSON answer (filters for a request) | **6-8 s** | ~50 s |
| Vision: one 768 px garment photo described | **24 s** (87 s on the first call) | ~180 s |
| Embedding: one text | 0.1 s | ~18 s (with model reloads) |
| Memory: text + embedding / vision + embedding / all three | **7.9 / 9.8 / 15.2 GB** | |

A server with 16 GB that also runs SQL Server and the website can hold **text + embedding or vision + embedding**, not all three: load only the models in use (see *Memory* below) or add RAM (32 GB holds all three).

**Use a recent LT ODM build with it.** This server applies the JSON schema but does not always end the answer: after the JSON it may go on writing, and with a vague instruction the model can get stuck inside one value. The app reads answers as a stream and stops at the end of the JSON, and stops with *"the AI service's answer ran on"* when one value passes 3,000 characters (see [AI overview](overview.md)). Builds before 2026-10-13 waited for such answers until the connection's timeout.

**1. Prepare the package (on any Windows PC with internet).** The server itself needs no internet access.

```powershell
# Program: download, check, unpack (the zip has an ovms\ folder). Needs the Visual C++ 2015-2022 x64 runtime (aka.ms/vs/17/release/VC_redist.x64.exe).
$v = "2026.4.1"
curl.exe -L -o ovms.zip "https://github.com/openvinotoolkit/model_server/releases/download/v$v/ovms_windows_${v}_python_off.zip"
curl.exe -L -o ovms.zip.sha256 "https://github.com/openvinotoolkit/model_server/releases/download/v$v/ovms_windows_${v}_python_off.zip.sha256"
(Get-FileHash ovms.zip -Algorithm SHA256).Hash   # must equal the value in ovms.zip.sha256
tar -xf ovms.zip
.\ovms\setupvars.ps1

# Models: download into a models folder and register them in its config.json
$m = "D:\transfer\models"
.\ovms\ovms.exe --pull --source_model OpenVINO/Qwen2.5-7B-Instruct-int4-ov --model_repository_path $m --model_name qwen2.5-7b --task text_generation --pipeline_type LM_CB --target_device CPU
.\ovms\ovms.exe --pull --source_model OpenVINO/Qwen2.5-VL-7B-Instruct-int4-ov --model_repository_path $m --model_name qwen2.5-vl-7b --task text_generation --pipeline_type VLM_CB --target_device CPU
.\ovms\ovms.exe --pull --source_model OpenVINO/Qwen3-Embedding-0.6B-int8-ov --model_repository_path $m --model_name qwen3-embedding-0.6b --task embeddings --pooling LAST --target_device CPU
.\ovms\ovms.exe --add_to_config --config_path "$m\config.json" --model_repository_path $m --model_name qwen2.5-7b --model_path "OpenVINO\Qwen2.5-7B-Instruct-int4-ov"
.\ovms\ovms.exe --add_to_config --config_path "$m\config.json" --model_repository_path $m --model_name qwen2.5-vl-7b --model_path "OpenVINO\Qwen2.5-VL-7B-Instruct-int4-ov"
.\ovms\ovms.exe --add_to_config --config_path "$m\config.json" --model_repository_path $m --model_name qwen3-embedding-0.6b --model_path "OpenVINO\Qwen3-Embedding-0.6B-int8-ov"
```

A pull that stops half-way resumes when run again. The model paths in `config.json` are relative, so the folder can be moved. Each model folder has a `graph.pbtxt` with its settings (e.g. `pooling: LAST` for the embedding model).

**2. Copy to the server.** `ovms\` to `C:\ovms`, the models folder to `C:\models`. Install the Visual C++ x64 runtime on the server if it is missing.

**3. Install the Windows service** (PowerShell as administrator):

```powershell
C:\ovms\install_ovms_service.bat C:\models
sc.exe config ovms start= auto            # the installer leaves it on manual start
sc.exe start ovms
```

- The service is called **ovms**, listens on port **8000** on **all network interfaces**, runs as **LocalSystem**, reads `C:\models\config.json` and logs to `C:\ovms\ovms_server.log` (also Event Viewer > Windows Logs > System).
- **Same machine as LT ODM:** make it listen only there. Edit the service command line to add `--rest_bind_address 127.0.0.1`, e.g. `sc.exe config ovms binPath= "\"C:\ovms\ovms.exe\" --rest_port 8000 --rest_bind_address 127.0.0.1 --config_path \"C:\models\config.json\" --log_level INFO --log_path \"C:\ovms\ovms_server.log\""`, then restart it.
- **Separate machine:** allow port 8000 only from the web server: `New-NetFirewallRule -DisplayName "OVMS from LT ODM" -Direction Inbound -Protocol TCP -LocalPort 8000 -RemoteAddress <web server IP> -Action Allow`. The server has **no sign-in**.
- **Protect the program and models folders:** Intel warns that whoever can change `config.json` (or `ovms.exe`) can run code as LocalSystem. Only administrators, SYSTEM and the account that looks after the server may write there. Run in PowerShell **as administrator**, with that account's name as `whoami` shows it (e.g. `ltodmsvr\istadmin`):

  ```powershell
  icacls "C:\ovms" /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F" "ltodmsvr\istadmin:(OI)(CI)F" /T
  icacls "C:\models" /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F" "ltodmsvr\istadmin:(OI)(CI)F" /T
  ```

  (With both under one folder, e.g. `E:\LTODM\ai-server`, one command on that folder covers both.) Naming the account avoids User Account Control asking for permission in Explorer; an account left out that is not an administrator loses access. If that happened, an administrator runs `takeown /f <folder> /r /d y` and then the command above. The website's app pool needs no access: it only calls port 8000.
- Stop / start / remove: `sc.exe stop ovms`, `sc.exe start ovms`, `sc.exe delete ovms`. The service reloads `config.json` when it changes.

**4. Test** (from the web server):

```powershell
.\tools\test-ai-server.ps1 -Endpoint http://<ai-server>:8000/v1 -TextModel qwen2.5-7b -VisionModel qwen2.5-vl-7b -EmbeddingModel qwen3-embedding-0.6b
```

The script is in the repository's `tools` folder and in every release (`tools\test-ai-server.ps1` next to `api\`); copy it to the server. Without a vision model on the server (e.g. text + embedding on a 16 GB server), add `-VisionModel ''` to skip that check. If PowerShell refuses to run it, start it with `powershell -ExecutionPolicy Bypass -File <path>\test-ai-server.ps1 ...`.

The server answers on both `/v1` and `/v3`. Use `/v1` in the app. Loading all models takes a minute or two after the service starts; the model list is empty until then. If the server cannot hold all three models, a request fails with *"Failed to allocate ... bytes of memory"* in `ovms_server.log`: remove a model (below).

**5. Point the app at it.** Settings > AI connections > Add connection: OpenAI-compatible, address `http://<ai-server>:8000/v1` (`http://localhost:8000/v1` on the same machine), **Runs on the LT network** ticked, timeout **300** s or more. Jobs: Text = `qwen2.5-7b`, Vision = `qwen2.5-vl-7b`, Embedding = `qwen3-embedding-0.6b`.

### OpenVINO image model (renders)

FLUX.1-schnell (Apache 2.0) draws renders, and with **Use the sketch** turns the style's flat sketch into a product photo (image-to-image through `images/edits`).

```powershell
.\ovms\ovms.exe --pull --source_model OpenVINO/FLUX.1-schnell-int4-ov --model_repository_path $m --task image_generation
.\ovms\ovms.exe --add_to_config --config_path "$m\config.json" --model_repository_path $m --model_name flux --model_path "OpenVINO\FLUX.1-schnell-int4-ov"
```

- **Download** 9.25 GB; about 18 GB of memory while drawing, on top of the text models.
- **CPU only:** add these lines under `models_path` in the model's `graph.pbtxt` (`start-local.ps1 -WithImage` adds them):

  ```
  device: "CPU"
  resolution: "256x256 512x512 256x384 384x256 512x768 768x512"
  default_resolution: "256x256"
  default_num_inference_steps: 3
  max_num_inference_steps: 8
  ```

  Without `device` the server tries Intel integrated graphics and fails with `CL_BUILD_PROGRAM_FAILURE`. Without `resolution` a 1024x1024 request (the app's default `ImageSize`) ran a 32 GB laptop out of memory and stopped the server (*Failed to allocate ... bytes of memory*); with it, other sizes are refused with a message. The 1.5x taller and wider sizes are for the app's shaped sketch canvases (`ImageFitShape`): a dress is drawn on 256x384, about 1.5 times the time of 256x256 but with room for details such as a back slit. FLUX.1-schnell is made for about 4 steps; 3 looked as good on a shirt test and drew about 40% faster (front and back at 256x384: 344 s and 333 s, 2026-10-16). The app sends no step count. Set `Ai:OpenAiCompatible:ImageSize` to `256x256` (or `512x512`) to match.
- **Measured:** a 256x256 render from a prompt, first after loading, 4 steps: 406 s. A shirt from its sketch, front and back at 256x384, 3 steps: 344 s + 333 s. Set the Image connection's timeout to 1800 s: it applies to each picture, and a view that passes it is lost (OpenVINO then logs *FATAL send node in loop* when it cannot deliver it; the server keeps running).
- **Measured on a laptop CPU** (Core i7-1270P, 32 GB, 2026-10-13): 256x256 renders took 68 s (1 step) to 95 s (2 steps) from a prompt, and 302-580 s from a sketch. 512x512 and larger are impractical on a CPU (estimated 20-40 minutes); **use a GPU server** for renders. Set the Image connection's timeout above the expected time.
- **How close to the sketch:** `Ai:OpenAiCompatible:ImageSketchStrength` (default 0.85). At 0.85 the dress test kept the neck, sleeves, length and back slit and looked like a photo; at 0.6 it stayed a clean drawing. Lower keeps more of the sketch, higher looks more real but may change the design. The sketch is cut to its garment drawings (pins, labels and stray lines left out) and centred on a white canvas of `ImageSize` with a 6% margin all round, not stretched (a drawing touching the edge came back with its hem cut off; a stretched one changes the garment's proportions).
- Job: Image = `flux` on the OpenVINO connection. With `ImageSize` 256x256 the API enlarges each render 4x (to 1024 px) with the Import pictures upscaler, about 1.3 s.
- **Developer PC:** `ai-server\start-local.ps1 -ImageOnly` (or `tools\start-dev.ps1 -ImageOnly`) serves FLUX **instead of** the text and embedding models (`models\config-local-image.json`) and adds the CPU settings if they are missing. Both together ran a 32 GB laptop out of memory: FLUX about 18 GB while drawing, the text model 8 GB, the usual programs about 16 GB. While it runs, text AI (Rewrite with AI, summaries, BOM explanations, Smart search AI, Library assistant) fails; the render description built from the BOM still works.
- **One server for both:** plan for 18 GB (FLUX) + 8 GB (text) + 2 GB (embedding) plus headroom, i.e. 48 GB or more, or a GPU for FLUX.

**Memory.** Each model in `config.json` is loaded at start and stays loaded. To leave one out (e.g. the vision model when picture descriptions are not used): `C:\ovms\ovms.exe --remove_from_config --config_path C:\models\config.json --model_name qwen2.5-vl-7b`; the service picks the change up by itself.

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
- Answer quality and speed depend on the models chosen. Renders keep the sketch guidance on servers with `images/edits` (OpenVINO Model Server), but need a GPU to be quick.
- Concept Studio, Cost Optimization and Market Trends, which send the most sensitive data (prices and costs), are the first to benefit.
