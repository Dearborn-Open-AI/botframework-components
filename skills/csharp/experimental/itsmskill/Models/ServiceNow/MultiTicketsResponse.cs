﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace ITSMSkill.Models.ServiceNow
{
    public class MultiTicketsResponse
    {
        public List<TicketResponse> result { get; set; }
    }
}
