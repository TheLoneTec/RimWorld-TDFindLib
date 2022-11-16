﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace TD_Find_Lib
{
	public class Settings : ModSettings, IFilterReceiver, IFilterProvider, IFilterStorageParent
	{
		private bool onlyAvailable = true;
		public bool OnlyAvailable => onlyAvailable != Event.current.shift && Find.CurrentMap != null;

		public static string defaultFiltersName = "Saved Filters";

		//Don't touch my filters
		internal List<FilterGroup> groupedFilters;
		public Settings()
		{
			SanityCheck();
			FilterTransfer.Register(this);
		}

		//IFilterStorageParent stuff
		//public void Write(); //in parent class
		public List<FilterGroup> Children => groupedFilters;
		public void Add(FilterGroup group)
		{
			Children.Add(group);
			group.parent = this;
		}

		internal void SanityCheck()
		{
			if (groupedFilters == null || groupedFilters.Count == 0)
			{
				groupedFilters = new();
				Add(new FilterGroup(defaultFiltersName, null));
			}
		}

		public void DoWindowContents(Rect inRect)
		{
			Listing_StandardIndent listing = new();
			listing.Begin(inRect);

			//Global Options
			listing.Header("Settings:");

			listing.CheckboxLabeled(
			"TD.OnlyShowFilterOptionsForAvailableThings".Translate(),
			ref onlyAvailable,
			"TD.ForExampleDontShowTheOptionMadeFromPlasteelIfNothingIsMadeFromPlasteel".Translate());

			listing.Gap();

			if(listing.ButtonTextLabeled("View all Find definitions", "View"))
			{
				//Ah gee this triggers settings.Write but that's no real problem
				Find.WindowStack.WindowOfType<Dialog_ModSettings>().Close();
				Find.WindowStack.WindowOfType<Dialog_Options>().Close();

				Find.WindowStack.Add(new TDFindLibListWindow(this));
			}

			listing.End();
		}


		public override void ExposeData()
		{
			Scribe_Values.Look(ref onlyAvailable, "onlyAvailable", true);

			Scribe_Collections.Look(ref groupedFilters, "groupedFilters", LookMode.Undefined, "??Group Name??", this);
			
			SanityCheck();
		}


		// FilterTransfer business
		public string Source => "Storage";
		public string ReceiveName => "Save";
		public string ProvideName => "Load";


		// IFilterReceiver things
		public FindDescription.CloneArgs CloneArgs => default; //save
		public bool CanReceive() => true;

		public void Receive(FindDescription desc)
		{
			//Save to groups
			if (groupedFilters.Count == 1)
			{
				// Only one group? skip this submenu
				SaveToGroup(desc, groupedFilters[0]);
			}
			else
			{
				//TODO: generalize this in FilterStorage if we think many Receivers are going to want to specify which group to receive?
				List<FloatMenuOption> submenuOptions = new();

				foreach (FilterGroup group in groupedFilters)
				{
					submenuOptions.Add(new FloatMenuOption("+ " + group.name, () => SaveToGroup(desc, group)));
				}

				Find.WindowStack.Add(new FloatMenu(submenuOptions));
			}
		}

		public static void SaveToGroup(FindDescription desc, FilterGroup group)
		{
			Find.WindowStack.Add(new Dialog_Name(desc.name, n => { desc.name = n; group.TryAdd(desc); }, $"Save to {group.name}"));
		}


		// IFilterProvider things
		public IFilterProvider.Method ProvideMethod()
		{
			return groupedFilters.Count > 1 ? IFilterProvider.Method.Grouping :
				(groupedFilters[0].Count == 0 ? IFilterProvider.Method.None : IFilterProvider.Method.Selection);
		}

		public FindDescription ProvideSingle() => null;
		public FilterGroup ProvideSelection() => groupedFilters[0];
		public List<FilterGroup> ProvideGrouping() => groupedFilters;
	}
}