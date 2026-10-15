# HID-Passthrough-Core (PSuite External)

## Description
A high-performance hardware-based input merging system using dual RP2040 microcontrollers. This project allows for real-time input offset compensation and peripheral emulation with 0ms latency.

## Key Features
- **Dual-Processor Isolation**: Separates data processing from HID emulation.
- **Hardware PIO USB Host**: Supports 1000Hz polling rate for gaming mice.
- **OLED Telemetry**: Real-time status monitoring via I2C display.
- **Physical Safety Switch**: Hardware-level bypass for manual control.

## Hardware Components
- 2x Raspberry Pi Pico (RP2040)
- USB-to-TTL Serial Bridge
- SSD1306 OLED Display
- Custom Macro Buttons