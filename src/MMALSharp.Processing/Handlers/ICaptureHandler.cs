﻿// <copyright file="ICaptureHandler.cs" company="Techyian">
// Copyright (c) Ian Auty. All rights reserved.
// Licensed under the MIT License. Please see LICENSE.txt for License info.
// </copyright>

using System;
using MMALSharp.Common;
using MMALSharp.Processors;

namespace MMALSharp.Handlers
{
    /// <summary>
    /// Provides the functionality to process user provided or captured image data.
    /// </summary>
    public interface ICaptureHandler : IDisposable
    {
        /// <summary>
        /// Returns a string of how much data has been processed by this capture handler.
        /// </summary>
        /// <returns>How much data has been processed by this capture handler.</returns>
        string TotalProcessed();
        
        /// <summary>
        /// Used to return user provided image data.
        /// </summary>
        /// <param name="allocSize">The count of bytes to return at most in the <see cref="ProcessResult"/>.</param>
        /// <returns>A <see cref="ProcessResult"/> object containing the user provided image data.</returns>
        ProcessResult Process(uint allocSize);

        /// <summary>
        /// Used to process the byte array containing our image data from an output port.
        /// </summary>
        /// <param name="data">A byte array containing image data.</param>
        void Process(byte[] data);

        /// <summary>
        /// Used for any further processing once we have completed capture.
        /// </summary>
        void PostProcess();
    }
}
