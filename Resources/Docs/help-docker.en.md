# Docker

Run an instance inside a Docker container instead of using a local binary. Useful when the backend you need (CUDA, ROCm, SYCL) is easier to get as a prebuilt image than to compile on the host.

The tab only appears when a Docker CLI is found. If it is missing, install Docker and restart the app.

## Settings

- **Run in Docker** — the master switch. While it is off the other fields do nothing and the server starts the usual way.
- **Image** — the image name, e.g. `ghcr.io/ggml-org/llama.cpp:server-cuda`. It must already be pulled or pullable.
- **Use all GPUs** — passes `--gpus all` to the container. Requires the NVIDIA Container Toolkit on the host.
- **Remove container on stop** — `--rm`, so stopped containers are not left behind.
- **Container name** — a fixed name instead of a random one. Handy for `docker logs`, but names must differ when running several instances at once.

## Things to watch

- **Paths are mounted from the host**: the model path must be visible inside the container. Keeping models in one directory and pointing **Models directory** at it is the simplest setup.
- The **port** from the **Main** tab is published, so you reach the server at the host address as usual.
- Feature detection (`llama-server --help`) runs against the binary set on the **Main** tab. If the image ships a different flag set, the support markers in the UI may not match reality.
- The full command, including `docker run`, is visible in the preview at the bottom of the window.
