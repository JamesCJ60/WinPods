# WinPods

[![Download Latest](https://img.shields.io/github/downloads/JamesCJ60/WinPods/total?style=flat-square&color=orange&label=Download%20Latest)](https://github.com/JamesCJ60/Universal-x86-Tuning-Utility/releases/latest)
[![Total Downloads](https://img.shields.io/github/downloads/JamesCJ60/WinPods/total?style=flat-square&color=orange&label=Download%20Total)](https://github.com/JamesCJ60/Universal-x86-Tuning-Utility/releases/latest)
[![discord](https://img.shields.io/discord/772105072720871435?color=orange&label=Discord&logo=discord&logoColor=white&style=flat-square)](https://discord.gg/9FeYVcbbUQ)

This project is still very much a WORK IN PROGRESS!

PLEASE READ THIS: THE SOFTWARE IS PROVIDED “AS IS” WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. MISUSE OF THIS SOFTWARE COULD CAUSE SYSTEM INSTABILITY OR MALFUNCTION.

If you come across any issues or errors with WinPods, please open an issue or ping `@jamescj` in our [discord community server](https://discord.gg/9FeYVcbbUQ). 

## What is WinPods?
- It's a lightweight utility that brings the native AirPods experience to Windows — the bits you'd normally only get on a Mac or iPhone.
- It's built with WPF + WPF-UI, so it lives quietly in your system tray with a frosted-glass, Mica summary that pops up the moment you open your case.
- It's a little alternative to Apple's own software for Windows PCs, giving you live left/right/case battery, automatic pause/resume when you remove or reinsert an earbud, automatic switching to your PC, one-AirPod playback, a driverless three-band equaliser with a live spectrum visualiser, and a Bluetooth telephony (microphone) toggle — no drivers or kernel hacks required.
- Works with AirPods (1st–4th generation, Pro, Pro 2, Pro 3 and Max).
- Works best on Windows 11 with a Bluetooth LE adapter.

## Disclaimers & Cautions
- If you intend to use WinPods in a video/text post online (e.g. YouTube, Reddit), please credit contributors by linking to the WinPods GitHub release page. We ask this so that viewers/readers can download the software from a trusted source, and so the developers get the proper recognition for their work.
- Contributors of this repo are not liable for any damages that may occur from using WinPods. Please use it at your own risk!
- AirPods, Apple, and the product imagery are trademarks or copyrighted materials of Apple Inc. They are included for product identification and are not covered by this project's open-source licence.

## Installation
- Head over to the [releases page](https://github.com/JamesCJ60/WinPods/releases)
- Click on the download hyperlink
- Once downloaded, extract the zip folder
- Once extrated, the application should now be installed
- Finally, find `WinPods.exe`, and open it 
- WinPods should now open, have fun!!!!

## Build and run

Requirements:

- Windows 10 version 2004 or newer, or Windows 11
- .NET 10 SDK
- A BLE-capable Bluetooth adapter

```powershell
dotnet build WinPods.slnx
dotnet run --project WinPods\WinPods.csproj
```

## Projects Used/Inspiration:
- [NAudio (GitHub)](https://github.com/naudio/NAudio)  
- [WPF UI (GitHub)](https://github.com/lepoco/wpfui)
- [LibrePods (GitHub)](https://github.com/kavishdevar/librepods)  
- [Vanara (GitHub)](https://github.com/dahall/Vanara)  
