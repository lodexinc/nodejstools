﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;

namespace Microsoft.NodejsTools.Jade {
    internal static class JadeFilters {
        public static bool IsFilter(string candidate) {
            string lower = candidate.ToLowerInvariant();
            return Array.BinarySearch<string>(_filters, lower) >= 0;
        }

        // must be sorted
        private static string[] _filters = new string[] { 
            "cdata",
            "coffeescript",
            "less",
            "markdown",
            "sass",
            "stylus",
        };
    }
}
