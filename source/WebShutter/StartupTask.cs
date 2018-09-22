/*
 * MIT License
 *
 * Copyright (c) by 2018 Simon Baer
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Windows.ApplicationModel.Background;
using Windows.Storage;

namespace WebShutter
{
    public sealed class StartupTask : IBackgroundTask
    {
        private BackgroundTaskDeferral deferral;
        private RestServer server;
        private GPIO gpio;

        // duration of a short pulse in milliseconds
        private readonly int ShortPulse = 100;

        // duration of a long pulse in milliseconds
        private readonly int LongPulse = 600;

        // delay in milliseconds after each pulse
        private readonly int DefaultDelay = 100;

        private Dictionary<int, ShutterGroup> shutters = new Dictionary<int, ShutterGroup>() {
            { 1, new ShutterGroup(9, 10) },
            { 2, new ShutterGroup(11, 17) },
            { 3, new ShutterGroup(18, 22) },
            { 4, new ShutterGroup(23, 24) },
        };

        /// <summary>
        /// Main method of the background task.
        /// </summary>
        /// <param name="taskInstance"></param>
        public void Run(IBackgroundTaskInstance taskInstance)
        {
            deferral = taskInstance.GetDeferral();
            gpio = new GPIO();
            gpio.InputChanged += Gpio_InputChanged;

            server = new RestServer();
            server.Initialise(ApplicationData.Current.LocalFolder);
            server.RestCommand += Server_RestCommand;
        }

        /// <summary>
        /// The onboard button has been pressed or released.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Gpio_InputChanged(object sender, Windows.Devices.Gpio.GpioPinValueChangedEventArgs e)
        {
            if (e.Edge == Windows.Devices.Gpio.GpioPinEdge.FallingEdge)
            {
                gpio.SetGPIO(gpio.LEDPin.ToString(), "1");
            }
            else
            {
                gpio.SetGPIO(gpio.LEDPin.ToString(), "0");
                Task.Delay(DefaultDelay).Wait();
                gpio.ResetOutput();
            }
        }

        /// <summary>
        /// Handle a REST command.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Server_RestCommand(object sender, RestCommandArgs e)
        {
            try
            {
                var parts = e.Command.ToLower().Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                if ((parts.Length >= 4) && (parts[0] == "api"))
                {
                    switch (parts[1])
                    {
                        case "gpio":
                            // switch GPIO pins directly, example: /api/gpio/17/1
                            e.IsValid = gpio.SetGPIO(parts[2], parts[3]);
                            break;
                        case "shutter":
                            // control a shutter group
                            e.IsValid = ShutterControl(parts[2], parts[3]);
                            break;
                        default:
                            // return "bad request"
                            e.IsValid = false;
                            e.ErrorMessage = $"\"{parts[1]}\" is an unknown api object. Valid objects are \"gpio\" or \"shutter\".";
                            break;
                    }
                }
                else
                {
                    // return "bad request"
                    e.IsValid = false;
                }
            }
            catch (Exception ex)
            {
                e.IsValid = false;
                e.ErrorMessage = "Exception: " + ex.Message;
            }
        }

        /// <summary>
        /// Output a sequence of GPIO pulses to control a window shutter.
        /// </summary>
        /// <param name="group">shutter group</param>
        /// <param name="sequence">sequence of commands</param>
        /// <returns>true if successful</returns>
        private bool ShutterControl(string group, string sequence)
        {
            int groupNumber;
            if (Int32.TryParse(group, out groupNumber))
            {
                var commands = sequence.ToLower().Split(',');

                // validate shutter group
                ShutterGroup shutterGroup;
                if (!shutters.TryGetValue(groupNumber, out shutterGroup))
                {
                    throw new ArgumentException($"Invalid shutter group {groupNumber}");
                }

                // validate commands
                foreach (string command in commands)
                {
                    int dummy = 0;
                    if ((command != "d") && (command != "dd") && (command != "u") && (command != "uu") && !Int32.TryParse(command, out dummy))
                    {
                        throw new ArgumentException($"Invalid command in sequence: {command}");
                    }
                    if ((dummy < 0 )|| (dummy > 10000))
                    {
                        throw new ArgumentException($"Invalid command in sequence: Delay out of range (0-10000).");
                    }
                }

                // execute commands
                bool addDelay = false;
                foreach (string command in commands)
                {
                    if (addDelay)
                    {
                        Task.Delay(DefaultDelay).Wait();
                    }
                    switch (command)
                    {
                        case "d":
                            gpio.Pulse(shutterGroup.DownPin, ShortPulse);
                            addDelay = true;
                            break;
                        case "dd":
                            gpio.Pulse(shutterGroup.DownPin, LongPulse);
                            addDelay = true;
                            break;
                        case "u":
                            gpio.Pulse(shutterGroup.UpPin, ShortPulse);
                            addDelay = true;
                            break;
                        case "uu":
                            gpio.Pulse(shutterGroup.UpPin, LongPulse);
                            addDelay = true;
                            break;
                        default:
                            Task.Delay(Int32.Parse(command)).Wait();
                            addDelay = false;
                            break;
                    }
                }
                return true;
            }
            throw new ArgumentException($"Invalid shutter group {group}.");
        }
    }
}
