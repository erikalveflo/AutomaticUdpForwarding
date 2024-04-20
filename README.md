# Automatic UDP forwarding

This is a protocol proposal meant to allow for automatic configuration of UDP forwarding in applications that consume UDP telemetry produced by real-time games and simulators.

The UDP telemetry is produced in real-time by games such as [DiRT Rally 2.0](https://en.wikipedia.org/wiki/Dirt_Rally_2.0) and consumed by applications such as [SimHub](https://www.simhubdash.com/), [SIM Dashboard](https://www.stryder-it.de/simdashboard/), [FlyPT Mover](https://www.flyptmover.com/home), [Sim Racing Studio](https://www.simracingstudio.com), [SimTools](https://www.xsimulator.net/), [Simrig Control Center](https://www.simrig.se/). The telemetry is crucial for these applications as it contains details about the player vehicle.

There are many ways to implement interprocess communication but some games choose to send its telemetry to a specific UDP port. Whoever binds and listens to that port first can receive telemetry. Problems arise however when multiple applications try to bind the same port. Only one application is allowed to bind the port; and only one application is allowed to receive telemetry. UDP telemetry is often forwarded from one application to the next to solve this issue. Unfortunately, the end-user must configure forwarding rules in each application while making sure to select unique ports for each.

This manual configuration step often leads to confusion for the end-user. This document aims to provide a solution. The solution is a UDP based protocol for automatically setting up forwarding rules between affected programs.

## Protocol proposal

See [Protocol.md](Protocol.md) for the proposed protocol.

[Join the discussion](https://github.com/erikalveflo/AutomaticUdpForwarding/discussions/1) about this proposal.

## Test implementations

This repository includes examples and tools that may help when implementing the protocol.

* [F1_2017](Emulators/F1_2017) - emulates telemetry sent by [F1 2017](https://en.wikipedia.org/wiki/F1_2017_(video_game))
* [DirtRally2](Emulators/DirtRally2) - emulates telemetry sent by [DiRT Rally 2.0](https://en.wikipedia.org/wiki/Dirt_Rally_2.0) (ExtraData=0)
* [SimHubPlugin](SimHubPlugin) - an implementation of the proposed protocol as a SimHub plug-in
* [CSharpExample](CSharpExample) - an implementation of the proposed protocol in C#

### Building
All code examples are built and tested using Visual Studio 2022 and .NET Framework 4.8. They probably work equally well with older versions of .NET (and newer) as well as Visual Studio 2017.
