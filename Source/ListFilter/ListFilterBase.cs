﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;
using UnityEngine;

namespace TD_Find_Lib
{
	// Introducin Thing Query: Like a ThingFilter, but more!
	public class ThingQueryDef : ThingQuerySelectableDef
	{
		public Type queryClass;

		public override IEnumerable<string> ConfigErrors()
		{
			if (queryClass == null)
				yield return "ThingQueryDef needs queryClass set";
		}
	}

	public abstract class ThingQuery : IExposable
	{
		public ThingQueryDef def;

		public IQueryHolder parent;

		public QuerySearch RootQuerySearch => parent?.RootQuerySearch;


		protected int id; //For Widgets.draggingId purposes
		private static int nextID = 1;
		protected ThingQuery() { id = nextID++; }


		private bool enabled = true; //simply turn off but keep in list
		public bool Enabled => enabled && DisableReason == null;
		public static readonly Color DisabledOverlayColor = Widgets.WindowBGFillColor * new Color(1,1,1,.5f);

		private bool _include = true; //or exclude
		public bool include
		{
			get => _include;
			private set
			{
				_include = value;
				_label = null;
			}
		}

		private string _label;
		public string Label
		{
			get {
				if (_label == null)
				{
					_label = def.LabelCap;
					if (!include)
						_label = "NOT".Colorize(Color.red);
				}
				return _label;
			}
		}


		// Okay, save/load. The basic gist here is:
		// During ExposeData loading, ResolveName is called for globally named things (defs)
		// But anything with a local reference (Zones) needs to resolve that ref on a map
		// Queries loaded from storage need to be cloned to a map to be used

		public virtual void ExposeData()
		{
			Scribe_Defs.Look(ref def, "def");
			Scribe_Values.Look(ref enabled, "enabled", true);
			Scribe_Values.Look(ref _include, "include", true);

			if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
			{
				DoResolveName();
			}
		}

		public virtual ThingQuery Clone()
		{
			ThingQuery clone = ThingQueryMaker.MakeQuery(def);
			clone.enabled = enabled;
			clone.include = include;


			//No - Will be set in QueryHolder.Add or QueryHolder's ExposeData on LoadingVars step
			//clone.parent = newHolder; 

			return clone;
		}
		public virtual void DoResolveName() { }
		public virtual void DoResolveRef(Map map) { }


		public void Apply( /* const */ List<Thing> inList, List<Thing> outList)
		{
			outList.Clear();

			foreach (Thing thing in inList)
				if (AppliesTo(thing))
					outList.Add(thing);
		}

		//This can be problematic for minified things: We want the qualities of the inner things,
		// but position/status of outer thing. So it just checks both -- but then something like 'no stuff' always applies. Oh well
		public bool AppliesTo(Thing thing)
		{
			bool applies = ApplesDirectlyTo(thing);
			if (!applies && thing.GetInnerThing() is Thing innerThing && innerThing != thing)
				applies = ApplesDirectlyTo(innerThing);

			return applies == include;
		}

		public abstract bool ApplesDirectlyTo(Thing thing);


		private bool shouldFocus;
		public void Focus() => shouldFocus = true;
		protected virtual void DoFocus() { }
		// Handle esc key before windows do
		public virtual bool OnCancelKeyPressed() => false;


		// Seems to be GameFont.Small on load so we're good
		public static float? incExcWidth;
		public static float IncExcWidth =>
			incExcWidth.HasValue ? incExcWidth.Value :
			(incExcWidth = Mathf.Max(Text.CalcSize("TD.IncludeShort".Translate()).x, Text.CalcSize("TD.ExcludeShort".Translate()).x)).Value;

		public (bool, bool) Listing(Listing_StandardIndent listing, bool locked)
		{
			Rect rowRect = listing.GetRect(Text.LineHeight + listing.verticalSpacing); //ends up being 22 which is height of Text.CalcSize 


			if (!include)
			{
				Widgets.DrawBoxSolid(rowRect.ContractedBy(2), new Color(1, 0, 0, 0.1f));
				GUI.color = new Color(1, 0, 0, 0.25f);
				Widgets.DrawLineHorizontal(rowRect.x + 2, rowRect.y + Text.LineHeight / 2, rowRect.width - 4);
				GUI.color = Color.white;
			}
			WidgetRow row = new WidgetRow(rowRect.xMax, rowRect.y, UIDirection.LeftThenDown, rowRect.width);

			bool changed = false;
			bool delete = false;

			if (!locked)
			{
				//Clear button
				if (row.ButtonIcon(TexCommand.ClearPrioritizedWork, "TD.DeleteThisQuery".Translate()))
				{
					delete = true;
					changed = true;
				}

				//Toggle button
				if (row.ButtonIcon(enabled ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex, "TD.EnableThisQuery".Translate()))
				{
					enabled = !enabled;
					changed = true;
				}

				//Include/Exclude
				if (row.ButtonText(include ? "TD.IncludeShort".Translate() : "TD.ExcludeShort".Translate(),
					"TD.IncludeOrExcludeThingsMatchingThisQuery".Translate(),
					fixedWidth: IncExcWidth))
				{
					include = !include;
					changed = true;
				}
			}


			//Draw option row
			rowRect.width -= (rowRect.xMax - row.FinalX);
			changed |= DrawMain(rowRect, locked);
			changed |= DrawUnder(listing, locked);
			if (shouldFocus)
			{
				DoFocus();
				shouldFocus = false;
			}

			if (DisableReason is string reason)
			{
				Widgets.DrawBoxSolidWithOutline(rowRect, DisabledBecauseReasonOverlayColor, Color.red);

				TooltipHandler.TipRegion(rowRect, reason);
			}

			if (!enabled)
			{
				Rect usedRect = rowRect;
				usedRect.yMax = listing.CurHeight;
				Widgets.DrawBoxSolid(usedRect, DisabledOverlayColor);
			}

			listing.Gap(listing.verticalSpacing);
			return (changed, delete);
		}


		public virtual bool DrawMain(Rect rect, bool locked)
		{
			Widgets.Label(rect, Label);
			return false;
		}
		protected virtual bool DrawUnder(Listing_StandardIndent listing, bool locked) => false;

		public virtual bool CurMapOnly => false;

		public virtual string DisableReason => null;
		public static readonly Color DisabledBecauseReasonOverlayColor = new Color(0.5f, 0, 0, 0.25f);

		public static void DoFloatOptions(List<FloatMenuOption> options)
		{
			if (options.NullOrEmpty())
				Messages.Message("TD.ThereAreNoOptionsAvailablePerhapsYouShouldUncheckOnlyAvailableThings".Translate(), MessageTypeDefOf.RejectInput, false);
			else
				Find.WindowStack.Add(new FloatMenu(options));
		}
	}

	public class FloatMenuOptionAndRefresh : FloatMenuOption
	{
		ThingQuery owner;
		public FloatMenuOptionAndRefresh(string label, Action action, ThingQuery f) : base(label, action)
		{
			owner = f;
		}

		public override bool DoGUI(Rect rect, bool colonistOrdering, FloatMenu floatMenu)
		{
			bool result = base.DoGUI(rect, colonistOrdering, floatMenu);

			if (result)
				owner.RootQuerySearch.RemakeList();

			return result;
		}
	}

	//automated ExposeData + Clone 
	public abstract class ThingQueryWithOption<T> : ThingQuery
	{
		// selection
		protected T _sel;
		protected string selName;// if UsesSaveLoadName,  = SaveLoadXmlConstants.IsNullAttributeName;
		private int _extraOption; //0 meaning use _sel, what 1+ means is defined in subclass

		// A subclass with extra fields needs to override ExposeData and Clone to copy them

		public string selectionError; // Probably set on load when selection is invalid (missing mod?)
		public override string DisableReason => selectionError;

		// would like this to be T const * sel;
		public ref T selByRef => ref _sel;
		public T sel
		{
			get => _sel;
			set
			{
				_sel = value;
				_extraOption = 0;
				selectionError = null;
				if (SaveLoadByName) selName = MakeSaveName();
				PostProcess();
				PostChosen();
			}
		}

		// A subclass should often set sel in the constructor
		// which will call the property setter above
		// If the default is null, and there's no PostSelected to do,
		// then it's fine to skip defining a constructor
		protected ThingQueryWithOption()
		{
			if (SaveLoadByName)
				selName = SaveLoadXmlConstants.IsNullAttributeName;
		}


		// PostProcess is called any time the selection is set: even after loading and cloning, etc.
		// PostChosen is called when the user selects the option (after a call to PostProcess)

		// A subclass with fields whose validity depends on the selection should override these
		//  PostProcess: to load extra data about the selection - MUST handle null.
		//   e.g. Specific Thing query sets the abs max range based on the stackLimit
		//   e.g. thoughts that have a range of stages, based on the selected def.
		//   e.g. the hediff query has a range of severity, which depends on the selected hediff, so the selectable range needs to be set here
		//  PostChosen: to set a default value, that is valid for the selection
		//   e.g. Specific Thing query sets the default chosen range based on the stackLimit
		//   e.g. NOT with the skill query which has a range 0-20, but that's valid for all skills, so no need to set per def
		// Most sublcasses needing PostChosen will also override PostProcess, to set the valid range and the default
		protected virtual void PostProcess() { }
		protected virtual void PostChosen() { }

		// This method works double duty:
		// Both telling if Sel can be set to null, and the string to show for null selection
		public virtual string NullOption() => null;

		protected int extraOption
		{
			get => _extraOption;
			set
			{
				_extraOption = value;
				_sel = default;
				selectionError = null;
				selName = null;
			}
		}

		//Okay, so, references.
		//A simple query e.g. string search is usable everywhere.
		//In-game, as an alert, in a saved search to load in, saved to file to load into another game, etc.
		//ExposeData and Clone can just copy that string, because a string is the same everywhere.
		//But a query that references in-game things can't be used universally.
		//Queries can be saved outside a running world, so even things like defs might not exist when loaded with different mods.
		//When such a query is run in-game, it does of course set 'sel' and reference it like normal
		//But when such a query is saved, it cannot be bound to an instance or even an ILoadReferencable id
		//So ExposeData saves and loads 'string selName' instead of the 'T sel'
		//When editing that query when inactive, that's fine, sel isn't set but selName is - so selName should be readable.
		//TODO: allow editing of selName: e.g. You can't add a "Stockpile Zone 1" query without that zone existing in-game.

		//ThingQuerys have 3 levels of saving, sort of like ExposeData's 3 passes.
		//Raw values can be saved/loaded by value easily in ExposeData.
		//Then there's UsesResolveName, and UsesResolveRef, which both SaveLoadByName
		//All SaveLoadByName queries are simply saved by a string name in ExposeData
		// - So it can be loaded into another game
		// - if that name cannot be resolved, the name is still kept instead of writing null
		//For loading, there's two different times to load:
		//Queries that UsesResolveName can be resolved after the game starts up (e.g. defs),
		// - ResolveName is called from ExposeData, ResolvingCrossRefs
		// - Queries that fail to resolve name are disabled until reset (at least, the DropDown subclasses)
		//Queries that UsesResolveRef must be resolved on a map (e.g. Zones, ILoadReferenceable)
		// - Queries that are loaded and inactive do not call ResolveRef and only have selName set.
		// - Queries get their refs resolved when a search is performed - and QuerySearch calls BindToMap()
		// - BindToMap will remember it's bound to that map and not bother to re-bind
		// - A Search that runs on multiple maps will bind to each map and resolve query refs dynamically.
		// - This of course will produce error messages if those can't be resolved on all maps

		protected readonly static bool IsDef = typeof(Def).IsAssignableFrom(typeof(T));
		protected readonly static bool IsRef = typeof(ILoadReferenceable).IsAssignableFrom(typeof(T));
		protected readonly static bool IsEnum = typeof(T).IsEnum;

		public virtual bool UsesResolveName => IsDef;
		public virtual bool UsesResolveRef => IsRef;
		private bool SaveLoadByName => UsesResolveName || UsesResolveRef;
		protected virtual string MakeSaveName() => sel?.ToString() ?? SaveLoadXmlConstants.IsNullAttributeName;

		public override void ExposeData()
		{
			base.ExposeData();

			Scribe_Values.Look(ref _extraOption, "ex");
			if (_extraOption > 0)
			{
				if (Scribe.mode == LoadSaveMode.LoadingVars)
					extraOption = _extraOption;	// property setter to set other fields null: TODO: they already are null, right?

				// No need to worry about sel or refname, we're done!
				return;
			}

			//Oh Jesus T can be anything but Scribe doesn't like that much flexibility so here we are:
			//(avoid using property 'sel' setter!)
			if (SaveLoadByName)
			{
				// Of course between games you can't get references so just save by name should be good enough
				// (even if it's from the same game, it can still resolve the reference all the same)

				// Saving a null selName saves "IsNull"
				Scribe_Values.Look(ref selName, "refName");

				// ResolveName() will be called on startup
				// ResolveRefs() will be called when a map is set
			}
			else if (typeof(IExposable).IsAssignableFrom(typeof(T)))
			{
				// TODO: I don't think any query uses this, 
				// It used to store a Query selection in here and it needed this
				// Oh well, might as well keep it around. Anything ILoadReferencable has already been handled and won't get here.
				Scribe_Deep.Look(ref _sel, "sel");
			}
			// Any subclass that RangeUB this has to ExposeData itself because I don't think we can force _sel to ref a struct
			else if (_sel is FloatRangeUB)
			{
				// Scribe_Values.Look(ref sel.range, "sel");
			}
			else if (_sel is IntRangeUB)
			{
				// Scribe_Values.Look(ref sel.range, "sel");
			}
			else
				Scribe_Values.Look(ref _sel, "sel");

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
				PostProcess();
		}

		public override ThingQuery Clone()
		{
			ThingQueryWithOption<T> clone = (ThingQueryWithOption<T>)base.Clone();

			clone.extraOption = extraOption;
			if (extraOption > 0)
				return clone;

			if (SaveLoadByName)
				clone.selName = selName;

			if(!UsesResolveRef)
				clone._sel = _sel;  //todo handle if sel needs to be deep-copied. Perhaps sel should be T const * sel...

			clone.selectionError = selectionError;

			return clone;
		}

		// Subclasses where SaveLoadByName is true need to override ResolveName() or ResolveRef()
		// (unless it's just a Def, already handled)
		// return matching object based on refName (refName will not be "null")
		// returning null produces a selection error and the query will be disabled
		public override void DoResolveName()
		{
			if (!UsesResolveName || extraOption > 0) return;

			if (selName == SaveLoadXmlConstants.IsNullAttributeName)
			{
				_sel = default; //can't use null because generic T isn't bound as reftype
			}
			else
			{
				_sel = ResolveName();

				if (_sel == null)
				{
					selectionError = $"Missing {def.LabelCap}: {selName}?";
					Verse.Log.Warning("TD.TriedToLoad0QueryNamed1ButCouldNotBeFound".Translate(def.LabelCap, selName));
				}
				else selectionError = null;
			}
		}
		public override void DoResolveRef(Map map)
		{
			if (!UsesResolveRef || extraOption > 0) return;

			if (map == null) return; //Not gonna go well

			if (selName == SaveLoadXmlConstants.IsNullAttributeName)
			{
				_sel = default; //can't use null because generic T isn't bound as reftype
			}
			else
			{
				_sel = ResolveRef(map);

				if (_sel == null)
				{
					selectionError = $"Missing {def.LabelCap}: {selName} on {map.Parent.LabelCap}?";
					Messages.Message("TD.TriedToLoad0QueryNamed1On2ButCouldNotBeFound".Translate(def.LabelCap, selName, map.Parent.LabelCap), MessageTypeDefOf.RejectInput, false);
				}
				else selectionError = null;
			}
		}

		protected virtual T ResolveName()
		{
			if (IsDef)
			{
				//Scribe_Defs.Look doesn't work since it needs the subtype of "Def" and T isn't boxed to be a Def so DefFromNodeUnsafe instead
				//_sel = ScribeExtractor.DefFromNodeUnsafe<T>(Scribe.loader.curXmlParent["sel"]);

				//DefFromNodeUnsafe also doesn't work since it logs errors - so here's custom code copied to remove the logging:

				return (T)(object)GenDefDatabase.GetDefSilentFail(typeof(T), selName, false);
			}

			throw new NotImplementedException();
		}
		protected virtual T ResolveRef(Map map)
		{
			throw new NotImplementedException();
		}
	}

	public abstract class ThingQueryDropDown<T> : ThingQueryWithOption<T>
	{
		private string GetLabel()
		{
			if (selectionError != null)
				return selName;

			if (extraOption > 0)
				return NameForExtra(extraOption);

			if (sel != null)
				return NameFor(sel);

			if (UsesResolveRef && !RootQuerySearch.active && selName != SaveLoadXmlConstants.IsNullAttributeName)
				return selName;

			return NullOption() ?? "??Null selection??";
		}

		public virtual IEnumerable<T> Options()
		{
			if (IsEnum)
				return Enum.GetValues(typeof(T)).OfType<T>();
			if (IsDef)
				return GenDefDatabase.GetAllDefsInDatabaseForDef(typeof(T)).Cast<T>();
			throw new NotImplementedException();
		}

		// Override this to group your T options into categories
		public virtual string CategoryFor(T def) => null;

		private Dictionary<string, List<T>> OptionCategories()
		{
			Dictionary<string, List<T>> result = new();
			foreach (T def in Options())
			{
				string cat = CategoryFor(def);

				List<T> options;
				if (!result.TryGetValue(cat, out options))
				{
					options = new();
					result[cat] = options;
				}

				options.Add(def);
			}
			return result;
		}


		public virtual bool Ordered => false;
		public virtual string NameFor(T o) => o is Def def ? def.LabelCap.RawText : typeof(T).IsEnum ? o.TranslateEnum() : o.ToString();
		public virtual string DropdownNameFor(T o) => NameFor(o);
		protected override string MakeSaveName()
		{
			if (sel is Def def)
				return def.defName;

			// Many subclasses will just use NameFor, so do it here.
			return sel != null ? NameFor(sel) : base.MakeSaveName();
		}

		public virtual int ExtraOptionsCount => 0;
		private IEnumerable<int> ExtraOptions() => Enumerable.Range(1, ExtraOptionsCount);
		public virtual string NameForExtra(int ex) => throw new NotImplementedException();

		public override bool DrawMain(Rect rect, bool locked)
		{
			bool changeSelection = false;
			bool changed = false;
			if (HasCustom)
			{
				// Label, Selection option button on left, custom on the remaining rect
				WidgetRow row = new WidgetRow(rect.x, rect.y);
				row.Label(Label);
				changeSelection = row.ButtonText(GetLabel());

				Rect customRect = rect;
				customRect.xMin = row.FinalX;
				changed = DrawCustom(customRect, row, rect);
			}
			else
			{
				//Just the label on left, and selected option button on right
				base.DrawMain(rect, locked);
				string label = GetLabel();
				Rect buttRect = rect.RightPart(0.4f);
				buttRect.xMin -= Mathf.Max(buttRect.width, Text.CalcSize(label).x) - buttRect.width;
				changeSelection = Widgets.ButtonText(buttRect, label);
			}
			if (changeSelection)
			{
				List<FloatMenuOption> options = new();

				if (NullOption() is string nullOption)
					options.Add(new FloatMenuOptionAndRefresh(nullOption, () => sel = default, this)); //can't null because T isn't bound as reftype

				if (UsesCategories)
				{
					Dictionary<string, List<T>> categories = OptionCategories();

					foreach (string catLabel in categories.Keys)
						options.Add(new FloatMenuOption(catLabel, () =>
						{
							List<FloatMenuOption> catOptions = new();
							foreach (T o in Ordered ? categories[catLabel].AsEnumerable().OrderBy(o => NameFor(o)).ToList() : categories[catLabel])
								catOptions.Add(new FloatMenuOptionAndRefresh(DropdownNameFor(o), () => sel = o, this));
							DoFloatOptions(catOptions);
						}));
				}
				else
				{
					foreach (T o in Ordered ? Options().OrderBy(o => NameFor(o)) : Options())
						options.Add(new FloatMenuOptionAndRefresh(DropdownNameFor(o), () => sel = o, this));
				}

				foreach (int ex in ExtraOptions())
					options.Add(new FloatMenuOptionAndRefresh(NameForExtra(ex), () => extraOption = ex, this));

				DoFloatOptions(options);
			}
			return changed;
		}

		// Subclass can override DrawCustom to draw anything custom
		// (otherwise it's just label and option selection button)
		// Use either rect or WidgetRow in the implementation
		public virtual bool DrawCustom(Rect rect, WidgetRow row, Rect fullRect) => throw new NotImplementedException();

		// Auto detection of subclasses that use DrawCustom:
		private static readonly HashSet<Type> customDrawers = null;
		private bool HasCustom => customDrawers?.Contains(GetType()) ?? false;

		// Auto detection of subclasses that use Dropdown Categories:
		private static readonly HashSet<Type> categoryUsers = null;
		private bool UsesCategories => categoryUsers?.Contains(GetType()) ?? false;
		static ThingQueryDropDown()//<T>	//Remember there's a customDrawers for each <T> but functionally that doesn't change anything
		{
			Type baseType = typeof(ThingQueryDropDown<T>);
			foreach (Type subclass in baseType.AllSubclassesNonAbstract())
			{
				if (subclass.GetMethod(nameof(DrawCustom)).DeclaringType != baseType)
				{
					if (customDrawers == null)
						customDrawers = new HashSet<Type>();

					customDrawers.Add(subclass);
				}

				if (subclass.GetMethod(nameof(CategoryFor)).DeclaringType != baseType)
				{
					if (categoryUsers == null)
						categoryUsers = new HashSet<Type>();

					categoryUsers.Add(subclass);
				}
			}
		}
	}

	public abstract class ThingQueryFloatRange : ThingQueryWithOption<FloatRangeUB>
	{
		public virtual float Min => 0f;
		public virtual float Max => 1f;
		public virtual ToStringStyle Style => ToStringStyle.PercentZero;

		public ThingQueryFloatRange() => sel = new FloatRangeUB(Min, Max);


		public override void ExposeData()
		{
			base.ExposeData();

			Scribe_Values.Look(ref _sel.range, "sel");
		}

		public override bool DrawMain(Rect rect, bool locked)
		{
			base.DrawMain(rect, locked);
			return TDWidgets.FloatRangeUB(rect.RightHalfClamped(Text.CalcSize(Label).x), id, ref selByRef, valueStyle: Style);
		}
	}
}
