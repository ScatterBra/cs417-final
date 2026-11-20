using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GameUI
{
    const int CatalogPageSize = 6;
    const int QuickBarCapacity = 4;
    readonly List<FurnitureItem> filteredFurniture = new List<FurnitureItem>();
    readonly List<FurnitureItem> pinnedFurniture = new List<FurnitureItem>();
    readonly List<(FurnitureCategory category, Image image)> categoryButtons =
        new List<(FurnitureCategory, Image)>();
    RectTransform catalogBody;
    RectTransform catalogCards;
    RectTransform quickBar;
    TMP_Text catalogPageLabel;
    TMP_Text catalogSortLabel;
    Button catalogPrevious;
    Button catalogNext;
    FurnitureCategory catalogCategory;
    bool catalogSortByPrice;
    int catalogPage;

    void BuildCatalogPanel(Transform root, float toolbarH)
    {
        if (bm == null || bm.grid == null) return;

        catalogPanel = MakeBar(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, toolbarH + 44f), new Vector2(760f, 72f));
        catalogPanel.name = "FurnitureCatalog";
        quickBar = MakePanel(catalogPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            Vector2.zero, new Vector2(760f, 72f));
        quickBar.name = "QuickBar";
        catalogBody = MakePanel(catalogPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 80f), new Vector2(760f, 342f));
        catalogBody.name = "Browser";
        var catalogColor = PanelColor;
        catalogColor.a = 0.96f;
        quickBar.GetComponent<Image>().color = catalogColor;
        catalogBody.GetComponent<Image>().color = catalogColor;

        MakeLabel(catalogBody, "Furniture", 24, new Vector2(-240f, 143f), new Vector2(250f, 32f));
        var sort = MakeButton(catalogBody, "Sort: Name", new Vector2(190f, 143f), new Vector2(180f, 36f), () =>
        {
            catalogSortByPrice = !catalogSortByPrice;
            catalogPage = 0;
            RefreshCatalog();
        });
        catalogSortLabel = sort.GetComponentInChildren<TMP_Text>();
        catalogSortLabel.fontSize = 18;
        MakeButton(catalogBody, "Hide", new Vector2(326f, 143f), new Vector2(80f, 36f),
            () => catalogBody.gameObject.SetActive(false)).GetComponentInChildren<TMP_Text>().fontSize = 18;

        var categories = (FurnitureCategory[])System.Enum.GetValues(typeof(FurnitureCategory));
        for (int i = 0; i < categories.Length; i++)
        {
            var category = categories[i];
            string label = category == FurnitureCategory.Auto ? "All" : category.ToString();
            var button = MakeButton(catalogBody, label, new Vector2(-318f + i * 106f, 94f),
                new Vector2(100f, 38f), () => FilterFurniture(category));
            button.GetComponentInChildren<TMP_Text>().fontSize = 16;
            categoryButtons.Add((category, button.GetComponent<Image>()));
        }

        catalogCards = MakeBar(catalogBody, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -22f), new Vector2(736f, 174f));
        catalogCards.name = "Cards";
        catalogPageLabel = MakeLabel(catalogBody, "", 17, new Vector2(0f, -143f), new Vector2(430f, 30f));
        catalogPrevious = MakeButton(catalogBody, "Prev", new Vector2(-308f, -143f), new Vector2(110f, 34f), () =>
        {
            catalogPage--;
            RefreshCatalog();
        });
        catalogNext = MakeButton(catalogBody, "Next", new Vector2(308f, -143f), new Vector2(110f, 34f), () =>
        {
            catalogPage++;
            RefreshCatalog();
        });
        catalogPrevious.GetComponentInChildren<TMP_Text>().fontSize = 18;
        catalogNext.GetComponentInChildren<TMP_Text>().fontSize = 18;
        RefreshCatalog();
        RefreshQuickBar();
        catalogPanel.gameObject.SetActive(false);
    }

    void FilterFurniture(FurnitureCategory category)
    {
        catalogCategory = category;
        catalogPage = 0;
        RefreshCatalog();
    }

    void RefreshCatalog()
    {
        filteredFurniture.Clear();
        foreach (var item in bm.grid.GetCatalog())
            if (item != null && (catalogCategory == FurnitureCategory.Auto || item.CatalogCategory == catalogCategory))
                filteredFurniture.Add(item);
        filteredFurniture.Sort((a, b) =>
        {
            int result = catalogSortByPrice ? a.price.CompareTo(b.price) : 0;
            if (result == 0) result = System.StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName);
            if (result == 0) result = System.StringComparer.Ordinal.Compare(a.name, b.name);
            return result;
        });

        int pages = Mathf.Max(1, Mathf.CeilToInt(filteredFurniture.Count / (float)CatalogPageSize));
        catalogPage = Mathf.Clamp(catalogPage, 0, pages - 1);
        catalogSortLabel.text = catalogSortByPrice ? "Sort: Price" : "Sort: Name";
        catalogPageLabel.text = $"{filteredFurniture.Count} items   |   {catalogPage + 1} / {pages}";
        catalogPrevious.interactable = catalogPage > 0;
        catalogNext.interactable = catalogPage + 1 < pages;
        foreach (var (category, image) in categoryButtons)
            image.color = category == catalogCategory ? AccentColor : ButtonColor;

        ClearCatalogChildren(catalogCards);
        int start = catalogPage * CatalogPageSize;
        int end = Mathf.Min(start + CatalogPageSize, filteredFurniture.Count);
        for (int i = start; i < end; i++)
        {
            var item = filteredFurniture[i];
            int slot = i - start;
            var card = MakePanel(catalogCards, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-246f + slot % 3 * 246f, 45f - slot / 3 * 90f), new Vector2(236f, 82f));
            card.name = "Item_" + item.name;
            var select = MakeButton(card, "", new Vector2(-36f, 0f), new Vector2(158f, 78f), () => ChooseFurniture(item));
            select.name = "Select_" + item.name;
            var swatch = MakePanel(select.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(5f, 0f), new Vector2(5f, 64f));
            swatch.GetComponent<Image>().color = item.color;
            swatch.GetComponent<Image>().raycastTarget = false;
            var title = MakeLabel(select.transform, item.DisplayName, 20, new Vector2(6f, 21f), new Vector2(136f, 26f), fontSerif);
            title.enableAutoSizing = true;
            title.fontSizeMin = 13;
            title.fontSizeMax = 20;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.raycastTarget = false;
            var details = MakeLabel(select.transform, $"${item.price:N0}\n{item.size.x:0.##} x {item.size.z:0.##} m",
                14, new Vector2(6f, -16f), new Vector2(138f, 40f));
            details.raycastTarget = false;
            bool pinned = pinnedFurniture.Contains(item);
            var pin = MakeButton(card, pinned ? "Unpin" : "Pin", new Vector2(82f, 0f), new Vector2(64f, 58f),
                () => ToggleFurniturePin(item));
            pin.GetComponentInChildren<TMP_Text>().fontSize = 15;
            pin.GetComponent<Image>().color = pinned ? AccentColor : ButtonColor;
        }
        if (filteredFurniture.Count == 0)
            MakeLabel(catalogCards, "No furniture in this category yet.", 22, Vector2.zero, new Vector2(700f, 50f), fontSerif);
    }

    void ToggleFurniturePin(FurnitureItem item)
    {
        if (pinnedFurniture.Contains(item))
        {
            pinnedFurniture.Remove(item);
            ShowToast(item.DisplayName + " unpinned");
        }
        else
        {
            if (pinnedFurniture.Count >= QuickBarCapacity)
            {
                ShowToast("Quick bar is full. Unpin an item first.");
                return;
            }
            pinnedFurniture.Add(item);
            ShowToast(item.DisplayName + " pinned to quick bar");
        }
        RefreshCatalog();
        RefreshQuickBar();
    }

    void RefreshQuickBar()
    {
        ClearCatalogChildren(quickBar);
        MakeButton(quickBar, "Browse", new Vector2(300f, 0f), new Vector2(132f, 52f), () =>
            catalogBody.gameObject.SetActive(!catalogBody.gameObject.activeSelf)).GetComponentInChildren<TMP_Text>().fontSize = 20;
        if (pinnedFurniture.Count == 0)
            MakeLabel(quickBar, "Pin favorites in Browse for quick access.", 20,
                new Vector2(-72f, 0f), new Vector2(570f, 48f), fontSerif).raycastTarget = false;
        for (int i = 0; i < pinnedFurniture.Count; i++)
        {
            var item = pinnedFurniture[i];
            var button = MakeButton(quickBar, item.DisplayName, new Vector2(-298f + i * 146f, 0f),
                new Vector2(138f, 52f), () => ChooseFurniture(item), fontSerif);
            button.name = "Quick_" + item.name;
            var label = button.GetComponentInChildren<TMP_Text>();
            label.enableAutoSizing = true;
            label.fontSizeMin = 13;
            label.fontSizeMax = 22;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    void ChooseFurniture(FurnitureItem item)
    {
        bm.SelectFurniture(item);
        catalogBody.gameObject.SetActive(false);
        ShowToast(item.DisplayName + ": tap to preview.");
    }

    static void ClearCatalogChildren(Transform parent)
    {
        foreach (Transform child in parent)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }

    void ToggleCatalog()
    {
        if (catalogPanel == null) return;
        SetCatalogVisible(!catalogPanel.gameObject.activeSelf);
    }

    void SetCatalogVisible(bool visible)
    {
        if (catalogPanel == null) return;
        catalogPanel.gameObject.SetActive(visible);
        if (visible) catalogBody.gameObject.SetActive(true);
    }
}
