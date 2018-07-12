using Sitecore;
using Sitecore.Configuration;
using Sitecore.ContentTesting;
using Sitecore.ContentTesting.Data;
using Sitecore.ContentTesting.Model.Data.Items;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Data.Managers;
using Sitecore.Diagnostics;
using Sitecore.Layouts;
using Sitecore.Links;
using Sitecore.Pipelines;
using Sitecore.Pipelines.ResolveRenderingDatasource;
using Sitecore.Publishing;
using Sitecore.Publishing.Pipelines.GetItemReferences;
using Sitecore.Publishing.Pipelines.PublishItem;
using Sitecore.Rules;
using Sitecore.Rules.Actions;
using Sitecore.Rules.ConditionalRenderings;
using Sitecore.Text;
using Sitecore.Workflows;
using Sitecore.XA.Foundation.JsonVariants;
using Sitecore.XA.Foundation.RenderingVariants;
using Sitecore.XA.Foundation.SitecoreExtensions.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace Sitecore.Support.XA.Foundation.Publication.Pipelines.GetItemReferences
{
  public class AddReferencedItemsToQueue: Sitecore.XA.Foundation.Publication.Pipelines.GetItemReferences.AddReferencedItemsToQueue
  {
    private readonly object _lock = new object();

    private HashSet<ID> _linkSet;

    private static List<ID> _childrenGroupingTemplateIds;

    private static List<ID> _compositeTemplateIds;

    protected static IList<ID> ChildrenGroupingTemplateIds => _childrenGroupingTemplateIds ?? (_childrenGroupingTemplateIds = (from id in Factory.GetStringSet("experienceAccelerator/sitecoreExtensions/childrenGroupingTemplates/template")
                                                                                                                               where ID.IsID(id)
                                                                                                                               select new ID(id)).ToList());
    protected static IList<ID> CompositeTemplateIds => _compositeTemplateIds ?? (_compositeTemplateIds = (from id in Factory.GetStringSet("experienceAccelerator/compositeTemplates/template")
                                                                                                          where ID.IsID(id)
                                                                                                          select new ID(id)).ToList());
    protected override List<Item> GetItemReferences(PublishItemContext context)
    {
      Assert.ArgumentNotNull(context, "context");
      Assert.ArgumentNotNull(context.PublishOptions, "options");
      List<Item> result = new List<Item>();
      if (context.PublishOptions.Mode == PublishMode.SingleItem)
      {
        lock (_lock)
        {
          return GetReferencedItems(context).ToList();
        }
      }
      return result;
    }

    protected override IList<Item> GetReferencedItems(PublishItemContext context)
    {
      _linkSet = new HashSet<ID>();
      Item itemToPublish = context.PublishHelper.GetItemToPublish(context.ItemId);
      if (itemToPublish != null && !IsInWorkflow(itemToPublish))
      {
        AddRenderingDataSourceReferences(itemToPublish, context.PublishOptions);
        AddAllReferences(itemToPublish, context.PublishOptions);
      }
      return (from id in _linkSet
              select context.PublishOptions.SourceDatabase.GetItem(id)).ToList();
    }

    protected override bool IsInWorkflow(Item item)
    {
      if (!TemplateManager.IsFieldPartOfTemplate(FieldIDs.Workflow, item))
      {
        return false;
      }
      IWorkflowProvider workflowProvider = item.Database.WorkflowProvider;
      if (workflowProvider != null && workflowProvider.GetWorkflows().Length != 0)
      {
        IWorkflow workflow = workflowProvider.GetWorkflow(item);
        if (workflow == null)
        {
          return false;
        }
        WorkflowState state = workflow.GetState(item);
        if (state != null)
        {
          return !state.FinalState;
        }
        return false;
      }
      return false;
    }

    protected override void AddRenderingDataSourceReferences(Item item, PublishOptions options)
    {
      LayoutField layoutField = new LayoutField(item);
      using (new ContextItemSwitcher(item))
      {
        DeviceItem[] all = item.Database.Resources.Devices.GetAll();
        foreach (DeviceItem device in all)
        {
          IEnumerable<RenderingReference> references = layoutField.GetReferences(device);
          foreach (RenderingReference item3 in references ?? Enumerable.Empty<RenderingReference>())
          {
            string[] items = new ListString(item3.Settings.DataSource ?? string.Empty).Items;
            foreach (string path in items)
            {
              Item item2 = options.SourceDatabase.GetItem(path, options.Language, Version.Latest);
              if (item2 != null)
              {
                AddReferencesForTree(item2, options);
              }
            }
            foreach (Rule<ConditionalRenderingsRuleContext> rule in item3.Settings.Rules.Rules)
            {
              foreach (RuleAction<ConditionalRenderingsRuleContext> action in rule.Actions)
              {
                SetDataSourceAction<ConditionalRenderingsRuleContext> setDataSourceAction = action as SetDataSourceAction<ConditionalRenderingsRuleContext>;
                if (setDataSourceAction != null)
                {
                  foreach (Item item4 in GetDatasourceItem(setDataSourceAction.DataSource, item, options))
                  {
                    AddReferencesForTree(item4, options);
                  }
                }
              }
            }
            var testingFactory = Sitecore.ContentTesting.ContentTestingFactory.Instance;
            var testStore = testingFactory.ContentTestStore as Sitecore.ContentTesting.Data.SitecoreContentTestStore;
            var values = testStore.GetMultivariateTestVariable(item3, item.Language)?.Values;
            IEnumerable<string> enumerable = values?.Where(v => v.Datasource != null).Select(v => v.Datasource.Uri.Path);

            if (enumerable != null)
            {
              foreach (string item5 in enumerable)
              {
                foreach (Item item6 in GetDatasourceItem(item5, item, options))
                {
                  AddReferencesForTree(item6, options);
                }
              }
            }
          }
        }
      }
    }

    protected override IEnumerable<Item> GetDatasourceItem(string datasource, Item item, PublishOptions options)
    {
      ResolveRenderingDatasourceArgs resolveRenderingDatasourceArgs = new ResolveRenderingDatasourceArgs(datasource);
      resolveRenderingDatasourceArgs.CustomData.Add("contextItem", item);
      CorePipeline.Run("resolveRenderingDatasource", resolveRenderingDatasourceArgs, false);
      datasource = (resolveRenderingDatasourceArgs.Datasource ?? string.Empty);
      return from d in new ListString(resolveRenderingDatasourceArgs.Datasource ?? string.Empty).Items
             select options.SourceDatabase.GetItem(datasource, options.Language, Version.Latest) into i
             where i != null
             select i;
    }

    protected override void AddReferencesForTree(Item datasourceItem, PublishOptions options)
    {
      AddAllReferences(datasourceItem, options);
      if (ChildrenGroupingTemplateIds.Any((ID templateId) => datasourceItem.Template.DoesTemplateInheritFrom(templateId)))
      {
        foreach (Item child in datasourceItem.Children)
        {
          AddReferencesForTree(child, options);
        }
      }
    }

    protected override void AddAllReferences(Item item, PublishOptions options)
    {
      if (_linkSet.Add(item.ID))
      {
        if (CompositeTemplateIds.Any((ID templateId) => item.Template.DoesTemplateInheritFrom(templateId)))
        {
          AddRenderingDataSourceReferences(item, options);
        }
        foreach (Item item3 in from il in item.Links.GetValidLinks(false)
                               select il.GetTargetItem())
        {
         if (item3!=null)
         {
          if (item3.Paths.IsContentItem || item3.Paths.IsMediaItem)
          {
            _linkSet.Add(item3.ID);
            if (item3.InheritsFrom(Sitecore.XA.Foundation.RenderingVariants.Templates.VariantDefinition.ID) || item3.InheritsFrom(Sitecore.XA.Foundation.JsonVariants.Templates.JsonVariantDefinition.ID))
            {
              Item[] descendants = item3.Axes.GetDescendants();
              foreach (Item item2 in descendants)
              {
                _linkSet.Add(item2.ID);
              }
            }
          }
         }
        }
      }
    }
  }
}
