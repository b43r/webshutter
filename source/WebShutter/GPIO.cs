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

using Windows.Devices.Gpio;

namespace WebShutter
{
    /// <summary>
    /// An interface to the Raspberry Pi 3 GPIO.
    /// </summary>
    internal class GPIO
    {
        // all available output GPIO pins
        private int[] outputPinNumbers = { 7, 9, 10, 11, 12, 13, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27 };

        // all available input GPIO pins
        private int[] inputPinNumbers = { 8 };

        // GPIO pin to which a LED is connected that is always pulsed
        public readonly int LEDPin = 7;

        private Dictionary<int, GpioPin> outputPins = new Dictionary<int, GpioPin>();
        private Dictionary<int, GpioPin> inputPins = new Dictionary<int, GpioPin>();

        public event EventHandler<GpioPinValueChangedEventArgs> InputChanged;

        /// <summary>
        /// Initialize all configured GPIO pins.
        /// </summary>
        public GPIO()
        {
            foreach (int pinNumber in outputPinNumbers)
            {
                var pin = GpioController.GetDefault().OpenPin(pinNumber);
                pin.Write(GpioPinValue.Low);
                pin.SetDriveMode(GpioPinDriveMode.Output);
                outputPins[pinNumber] = pin;
            }

            foreach (int pinNumber in inputPinNumbers)
            {
                var pin = GpioController.GetDefault().OpenPin(pinNumber);
                pin.SetDriveMode(GpioPinDriveMode.InputPullUp);
                pin.ValueChanged += Pin_ValueChanged;
                inputPins[pinNumber] = pin;
            }

            // ready
            Pulse(LEDPin, 100);
            Task.Delay(100).Wait();
            Pulse(LEDPin, 100);
        }

        /// <summary>
        /// A input pin has changed it's value.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void Pin_ValueChanged(GpioPin sender, GpioPinValueChangedEventArgs args)
        {
            InputChanged?.Invoke(sender, args);
        }

        /// <summary>
        /// Set a GPIO pin to a given state.
        /// </summary>
        /// <param name="pin">pin number</param>
        /// <param name="value">0=low, 1=high</param>
        /// <returns>true if successful</returns>
        public bool SetGPIO(string pin, string value)
        {
            int pinNumber;
            int valueNumber;
            if (Int32.TryParse(pin, out pinNumber))
            {
                if (Int32.TryParse(value, out valueNumber))
                {
                    if (outputPins.ContainsKey(pinNumber))
                    {
                        outputPins[pinNumber].Write(valueNumber == 0 ? GpioPinValue.Low : GpioPinValue.High);
                        return true;
                    }
                    throw new ArgumentException($"Invalid GPIO pin {pin}.");
                }
                throw new ArgumentException($"Invalid GPIO value {value}.");
            }
            throw new ArgumentException($"Invalid GPIO pin {pin}.");
        }

        /// <summary>
        /// Pulse a GPIO pin from 0 to 1 for the given duration.
        /// </summary>
        /// <param name="pin">pin number</param>
        /// <param name="duration">duration in milliseconds</param>
        public void Pulse(int pin, int duration)
        {
            SetGPIO(pin.ToString(), "1");
            SetGPIO(LEDPin.ToString(), "1");
            Task.Delay(duration).Wait();
            SetGPIO(pin.ToString(), "0");
            SetGPIO(LEDPin.ToString(), "0");
        }

        /// <summary>
        /// Reset all output pins to 0.
        /// </summary>
        public void ResetOutput()
        {
            foreach (int pin in outputPinNumbers)
            {
                SetGPIO(pin.ToString(), "0");
                Pulse(LEDPin, 10);
                Task.Delay(50).Wait();
            }
        }
    }
}
