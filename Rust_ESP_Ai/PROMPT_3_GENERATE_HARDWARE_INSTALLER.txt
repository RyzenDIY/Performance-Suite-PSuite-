Task: Create a setup script (`gpu_setup.bat`) and hardware routing logic that configures the AI environment depending on whether the user has an NVIDIA or AMD GPU.

Requirements:
1. NVIDIA Path: Install `onnxruntime-gpu` and the explicit PyTorch build for CUDA (e.g., matching CUDA Toolkit 12.1 using `--index-url https://pytorch.org`). Force `CUDAExecutionProvider` as primary.
2. AMD Path: Configure fallback for AMD graphics cards using DirectML or ROCm execution providers (`DmlExecutionProvider`). Install the appropriate pip modules to avoid crashes.
3. CPU Safe Fallback: If no compatible GPU drivers are found in the system, automatically fall back to `CPUExecutionProvider` without throwing a crash loop, while warning the user.
4. NumPy Conflict Fix: Ensure the installation strictly enforces a compatible NumPy version (e.g., `numpy<2`) to prevent the missing `_ARRAY_API` attribute crash.

Generate the batch file logic and Python verification scripts.
