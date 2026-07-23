# WinPods

[![Download Latest](https://img.shields.io/github/downloads/JamesCJ60/WinPods/total?style=flat-square&color=orange&label=Download%20Latest)](https://github.com/JamesCJ60/WinPods/releases/latest)
[![Total Downloads](https://img.shields.io/github/downloads/JamesCJ60/WinPods/total?style=flat-square&color=orange&label=Download%20Total)](https://github.com/JamesCJ60/WinPods/releases/latest)
[![discord](https://img.shields.io/discord/772105072720871435?color=orange&label=Discord&logo=discord&logoColor=white&style=flat-square)](https://discord.gg/9FeYVcbbUQ)

<img width="2494" height="555" alt="GitHub Repo Banner Image" src="https://github.com/user-attachments/assets/00fa7888-9ccc-4086-9c5c-013129041bd9" />

This project is still very much a WORK IN PROGRESS!

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY ARISING FROM THE USE OF THIS SOFTWARE.

If you come across any issues or errors with WinPods, please open an issue or ping `@jamescj` in our [discord community server](https://discord.gg/9FeYVcbbUQ). 

## What is WinPods?
- It's a lightweight utility that brings the native AirPods experience to Windows, just like you'd normally only get on a Mac or iPhone.
- It's built with WPF + WPF-UI, so it lives quietly in your system tray with a summary that pops up the moment you open your case.
- It's a little alternative to Apple's own software ecosystem that is intended for Windows PCs, giving you live left/right/case battery, automatic pause/resume when you remove or reinsert an earbud, automatic switching to your PC, one-AirPod playback, a driverless three-band equaliser with a live spectrum visualiser, and a Bluetooth telephony (microphone) toggle with no drivers or kernel hacks required.
- Works with AirPods (1st–4th generation, Pro, Pro 2, Pro 3, Max, and Max 2).
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
- WinPods should now open as a tray icon, have fun!!!!

Tray icon for reference: <img width="27" height="27" alt="image" src="https://github.com/user-attachments/assets/17356511-384b-45b1-b966-5d70c311cc95" />


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

## User Interface Examples
### Status Messages:
<img width="463" height="157" alt="image" src="https://github.com/user-attachments/assets/3436cab3-386c-4cf0-8e82-8e38de0a7dc4" />
<img width="462" height="154" alt="image" src="https://github.com/user-attachments/assets/78243d4f-20a8-4304-8857-a31234abcb6c" />
<img width="449" height="143" alt="image" src="https://github.com/user-attachments/assets/abf47448-83ae-4995-8297-d79b547f8c43" />

### Main Menu:
<img width="521" height="659" alt="image" src="https://github.com/user-attachments/assets/3a269de1-9e6f-4810-a048-6d243ad0256a" />
<img width="526" height="664" alt="image" src="https://github.com/user-attachments/assets/3883dcdf-282a-4799-b826-afd4e3d24b4f" />
