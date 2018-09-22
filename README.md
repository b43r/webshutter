# WebShutter

WebShutter implements a simple webserver and REST API to controll GPIO pins of a Raspberry Pi. WebShutter runs as a UWP (Universal Windows Platform) on Windows 10 IOT.

## Getting started

You need a Raspberry Pi (at least model 2). Install Windows 10 IoT Core (see https://docs.microsoft.com/de-de/windows/iot-core/tutorials/tutorials).

## Hardware wiring

Our shutter hardware offers the possibility to connect push buttons for up/down commands. The connectors supply a voltage of 15V and a current of 1mA is flowing when connected. To avoid any damage to the Raspberry or shutter hardware I wanted to galvanically isolate both systems using optocouplers.

A [breakout board](https://www.seeedstudio.com/Raspberry-Pi-Breakout-Board-v1-0-p-2410.html) from [Seed](https://www.seeedstudio.com/) is used, which can be screwed ontop of the Raspberry using spacers.

![Seed breakout board](images/breakoutboard.jpg)

For the optocouplers I went for the ILQ615-4 which contains 4 galvanically isolated switches in a DIL 16 package:

![Optocoupler](images/optocoupler.png)

