﻿using System;
using System.Diagnostics;
using Emignatik.NxFileViewer.Models.TreeItems;
using Emignatik.NxFileViewer.Models.TreeItems.Impl;
using Emignatik.NxFileViewer.Views.TreeItems.Impl;

namespace Emignatik.NxFileViewer.Views.TreeItems;

public class ItemViewModelBuilder : IItemViewModelBuilder
{
    private readonly IServiceProvider _serviceProvider;

    public ItemViewModelBuilder(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public IItemViewModel Build(IItem item)
    {
        switch (item)
        {
            case XciItem xciItem:
                return new XciItemViewModel(xciItem, _serviceProvider);

            case XciPartitionItem xciPartitionItem:
                return new XciPartitionItemViewModel(xciPartitionItem, _serviceProvider);

            case NspItem nspItem:
                return new NspItemViewModel(nspItem, _serviceProvider);

            case NcaItem ncaItem:
                return new NcaItemViewModel(ncaItem, _serviceProvider);    
                
            case TicketItem ticketItem:
                return new TicketItemViewModel(ticketItem, _serviceProvider);

            case PartitionFileEntryItemBase partitionFileEntryItem:
                return new PartitionFileEntryItemViewModel(partitionFileEntryItem, _serviceProvider);

            case FsSectionItem fsSectionItem:
                return new FsSectionItemViewModel(fsSectionItem, _serviceProvider);    
            
            case PatchSectionItem patchSectionItem:
                return new PatchSectionItemViewModel(patchSectionItem, _serviceProvider);

            case CnmtItem cnmtItem:
                return new CnmtItemViewModel(cnmtItem, _serviceProvider);

            case NacpItem nacpItem:
                return new NacpItemViewModel(nacpItem, _serviceProvider);

            case MainItem mainItem:
                return new MainItemViewModel(mainItem, _serviceProvider);

            case DirectoryEntryItem directoryEntryItem:
                return new DirectoryEntryItemViewModel(directoryEntryItem, _serviceProvider);      
                
            case CnmtContentEntryItem cnmtContentEntryItem:
                return new CnmtContentEntryItemViewModel(cnmtContentEntryItem, _serviceProvider);

            default:
                Debug.Fail($"{nameof(IItemViewModel)} implementation missing for item of type «{item.GetType().Name}».");
                return new ItemViewModel(item, _serviceProvider);
        }
    }
}