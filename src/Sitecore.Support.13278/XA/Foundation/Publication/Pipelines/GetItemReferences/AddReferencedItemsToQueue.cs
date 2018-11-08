using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Links;
using Sitecore.Publishing;
using Sitecore.Publishing.Pipelines.PublishItem;
using Sitecore.XA.Foundation.SitecoreExtensions.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Sitecore.Support.XA.Foundation.Publication.Pipelines.GetItemReferences
{
  public class AddReferencedItemsToQueue : Sitecore.XA.Foundation.Publication.Pipelines.GetItemReferences.AddReferencedItemsToQueue
  {
    private HashSet<ID> _linkSet;

    protected override IList<Item> GetReferencedItems(PublishItemContext context)
    {
      this._linkSet = new HashSet<ID>();
      Item itemToPublish = context.PublishHelper.GetItemToPublish(context.ItemId);
      if (itemToPublish != null && !this.IsInWorkflow(itemToPublish))
      {
        this.AddRenderingDataSourceReferences(itemToPublish, context.PublishOptions);
        this.AddAllReferences(itemToPublish, context.PublishOptions);
      }
      return (IList<Item>)this._linkSet.Select<ID, Item>((Func<ID, Item>)(id => context.PublishOptions.SourceDatabase.GetItem(id))).ToList<Item>();
    }

    protected override void AddAllReferences(Item item, PublishOptions options)
    {
      if (!this._linkSet.Add(item.ID))
        return;
      if (AddReferencedItemsToQueue.CompositeTemplateIds.Any<ID>((Func<ID, bool>)(templateId => item.Template.DoesTemplateInheritFrom(templateId))))
        this.AddRenderingDataSourceReferences(item, options);
      foreach (Item referencedItem in ((IEnumerable<ItemLink>)item.Links.GetValidLinks(false)).Select<ItemLink, Item>((Func<ItemLink, Item>)(il => il.GetTargetItem())))
      {
        #region FIX 13278
        if (referencedItem != null)
        #endregion
        {
          if (referencedItem.Paths.IsContentItem || referencedItem.Paths.IsMediaItem)
          {
            this._linkSet.Add(referencedItem.ID);
            if (referencedItem.InheritsFrom(Sitecore.XA.Foundation.RenderingVariants.Templates.VariantDefinition.ID) || referencedItem.InheritsFrom(Sitecore.XA.Foundation.JsonVariants.Templates.JsonVariantDefinition.ID))
            {
              foreach (Item descendant in referencedItem.Axes.GetDescendants())
                this._linkSet.Add(descendant.ID);
            }
          }
        }
      }
    }
  }
}