using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 一顆天體（恆星、行星、衛星）的軌道參數與顯示設定。資料來源是 Assets/Resources/Planets.csv（可用 Excel 開啟編輯）。
/// </summary>
[System.Serializable]
public class PlanetSpec
{
    public string name;
    public string parent;        // 繞著哪顆天體轉；空白 = 放在原點靜止（例如太陽）
    public double mass;          // 太陽質量
    public double a;             // 相對 parent 的半長軸 (AU)
    public double e;             // 離心率
    public float displayRadius;  // Unity 單位，純顯示用
    public Color color;
    public float orbitScale;     // 顯示時把與 parent 的距離放大幾倍，純顯示用（月球需要，否則會被地球蓋住）
    public bool retrograde;      // 逆行：繞 parent 的方向與其他天體相反（例如海衛一 Triton）
    public string prefab;        // Prefab 在 Resources 底下的路徑（不含副檔名），例如 Prefabs/Planets/Earth；空白 = 純色球
    public double rotationPeriod; // 恆星自轉週期（天），純顯示用；正 = 繞自轉軸逆時針，負 = 反向，0 = 不自轉

    /// <summary>
    /// 解析 CSV。第一列是標題：name,parent,mass,a,e,displayRadius,color,orbitScale,retrograde,prefab,rotationPeriod。
    /// color 用 #RRGGBB；orbitScale 可省略（預設 1）；retrograde 可省略（預設 0，填 1 = 逆行）；prefab 可省略（空白 = 純色球）；
    /// rotationPeriod 可省略（預設 0 = 不自轉）。
    /// 空白列與 # 開頭的列會略過。分隔符號接受逗號或分號。
    /// </summary>
    public static PlanetSpec[] ParseCsv(string text)
    {
        var result = new List<PlanetSpec>();
        var inv = CultureInfo.InvariantCulture;
        string[] lines = text.TrimStart('\uFEFF').Split('\n');

        for (int i = 1; i < lines.Length; i++)   // 第 0 列是標題
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            string[] c = line.Split(line.Contains(";") ? ';' : ',');
            if (c.Length < 7)
            {
                Debug.LogError($"[Planets.csv] 第 {i + 1} 列欄位不足 7 欄：{line}");
                continue;
            }

            try
            {
                if (!ColorUtility.TryParseHtmlString(c[6].Trim(), out Color color))
                {
                    Debug.LogWarning($"[Planets.csv] 第 {i + 1} 列顏色無法解析：{c[6]}，改用白色");
                    color = Color.white;
                }

                result.Add(new PlanetSpec
                {
                    name = c[0].Trim(),
                    parent = c[1].Trim(),
                    mass = double.Parse(c[2].Trim(), NumberStyles.Float, inv),
                    a = double.Parse(c[3].Trim(), NumberStyles.Float, inv),
                    e = double.Parse(c[4].Trim(), NumberStyles.Float, inv),
                    displayRadius = float.Parse(c[5].Trim(), NumberStyles.Float, inv),
                    color = color,
                    orbitScale = c.Length > 7 && c[7].Trim().Length > 0
                        ? float.Parse(c[7].Trim(), NumberStyles.Float, inv) : 1f,
                    retrograde = c.Length > 8 && c[8].Trim() == "1",
                    prefab = c.Length > 9 ? c[9].Trim() : "",
                    rotationPeriod = c.Length > 10 && c[10].Trim().Length > 0
                        ? double.Parse(c[10].Trim(), NumberStyles.Float, inv) : 0.0,
                });
            }
            catch (System.FormatException)
            {
                Debug.LogError($"[Planets.csv] 第 {i + 1} 列數字格式錯誤：{line}");
            }
        }
        return result.ToArray();
    }
}
