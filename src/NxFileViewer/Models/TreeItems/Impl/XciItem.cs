﻿using System;
using System.Collections.Generic;
using LibHac.Common.Keys;
using LibHac.Fs.Fsa;
using LibHac.Tools.Fs;

namespace Emignatik.NxFileViewer.Models.TreeItems.Impl;

public class XciItem : ItemBase
{
    private readonly IFile _file;

    public XciItem(Xci xci, string name, IFile file, KeySet keySet) : base(null)
    {
        Xci = xci ?? throw new ArgumentNullException(nameof(xci));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _file = file ?? throw new ArgumentNullException(nameof(file));
        KeySet = keySet ?? throw new ArgumentNullException(nameof(keySet));
    }

    public override List<XciPartitionItem> ChildItems { get; } = new();

    public Xci Xci { get; }

    public override string LibHacTypeName => Xci.GetType().Name;

    public override string? Format => "XCI";

    public override string Name { get; }

    public override string DisplayName => Name;

    public KeySet KeySet { get; }

    public override void Dispose()
    {
        _file.Dispose();
    }
}