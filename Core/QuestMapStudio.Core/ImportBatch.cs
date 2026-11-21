using System;

namespace QuestMapStudio.Core;

public sealed record ImportBatch(DateTimeOffset Time, string[] Folders);
