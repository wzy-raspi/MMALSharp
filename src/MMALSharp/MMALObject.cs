﻿// <copyright file="MMALObject.cs" company="Techyian">
// Copyright (c) Ian Auty. All rights reserved.
// Licensed under the MIT License. Please see LICENSE.txt for License info.
// </copyright>

using System;

namespace MMALSharp
{
    /// <summary>
    /// This class is the base class for all MMAL components.
    /// </summary>
    public abstract class MMALObject : IDisposable, IMMALObject
    {
        /// <summary>
        /// Creates a new instance of the MMALObject class and adds this instance to the Objects list.
        /// </summary>
        protected MMALObject()
        {
        }

        /// <summary>
        /// Removes this instance from the Objects list.
        /// </summary>
        public virtual void Dispose()
        {
        }

        /// <inheritdoc />
        public abstract bool CheckState();
    }
}
