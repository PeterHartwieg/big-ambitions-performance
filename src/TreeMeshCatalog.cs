using System.Collections.Generic;
namespace CityPerformance
{
    // Asset metadata only. Manhattan: audited Windows assets; Hamptons: local assets,
    // accepted on Windows only when both live mesh names, vertex and triangle-index counts match.
    public static class TreeMeshCatalog
    {
        private readonly struct Pair
        {
            public readonly string Low; public readonly int HighVertices, LowVertices; public readonly long HighIndices, LowIndices;
            public Pair(int hv, long hi, string low, int lv, long li) { HighVertices=hv; HighIndices=hi; Low=low; LowVertices=lv; LowIndices=li; }
        }
        private static readonly Dictionary<string, Pair> pairs = new Dictionary<string, Pair>
        {
            { "Cypress_LOD0", new Pair(3887, 5811, "Cypress_LOD1", 2506, 3498) },
            { "TreeBirch_LOD0", new Pair(2331, 6120, "TreeBirch_LOD1", 1400, 2148) },
            { "TreeBroadleaf_L1_LOD0", new Pair(5578, 16704, "TreeBroadleaf_L1_LOD1", 3897, 9672) },
            { "TreeBroadleaf_M1_LOD0", new Pair(1727, 5532, "TreeBroadleaf_M1_LOD1", 1146, 3048) },
            { "TreeBroadleaf_M2_LOD0", new Pair(1898, 5976, "TreeBroadleaf_M2_LOD1", 1351, 3630) },
            { "TreeBroadleaf_XL1_LOD0", new Pair(10264, 28878, "TreeBroadleaf_XL1_LOD1", 6899, 14676) },
            { "TreeBroadleaf_XL2_LOD0", new Pair(5303, 13398, "TreeBroadleaf_XL2_LOD1", 2625, 5118) },
            { "TreeMaple_LOD0", new Pair(11706, 26724, "TreeMaple_LOD1", 7902, 14871) },
            { "TreeOak_LOD0", new Pair(9817, 20064, "TreeOak_LOD1", 5684, 9918) },
            { "TreePine_B_L1_LOD0", new Pair(9086, 20964, "TreePine_B_L1_LOD1", 7155, 14412) },
            { "TreePine_B_M1_LOD0", new Pair(5749, 12546, "TreePine_B_M1_LOD1", 2996, 5040) },
            { "TreePine_B_XL1_LOD0", new Pair(13142, 34878, "TreePine_B_XL1_LOD1", 9805, 22278) },
            { "TreePine_L1_LOD0", new Pair(1221, 4128, "TreePine_L1_LOD1", 818, 2628) },
            { "TreePine_L2_LOD0", new Pair(1869, 6444, "TreePine_L2_LOD1", 1404, 4446) },
            { "TreePine_S1_LOD0", new Pair(459, 1590, "TreePine_S1_LOD1", 200, 534) },
            { "TreePine_XL1_LOD0", new Pair(1930, 6240, "TreePine_XL1_LOD1", 972, 3030) },
            { "acacia_1_LOD0", new Pair(5512, 16524, "acacia_1_LOD1", 2945, 5280) },
            { "acacia_2_LOD0", new Pair(4837, 13524, "acacia_2_LOD1", 2788, 4800) },
            { "beech_1_LOD0", new Pair(6314, 14190, "beech_1_LOD1", 3773, 4035) },
            { "beech_2_LOD0", new Pair(10462, 20388, "beech_2_LOD1", 7861, 9093) },
            { "birch_1_LOD0", new Pair(2840, 6639, "birch_1_LOD1", 1368, 2475) },
            { "birch_3_LOD0", new Pair(6300, 17529, "birch_3_LOD1", 3557, 6504) },
            { "dry_tree_1_LOD0", new Pair(11883, 22926, "dry_tree_1_LOD1", 9002, 10125) },
            { "fir_1_LOD0", new Pair(567, 2196, "fir_1_LOD1", 242, 438) },
            { "fir_4_LOD0", new Pair(942, 3096, "fir_4_LOD1", 517, 765) },
            { "juniper_1_LOD0", new Pair(1130, 2784, "juniper_1_LOD1", 531, 828) },
            { "maple_1_LOD0", new Pair(10255, 17235, "maple_1_LOD1", 7882, 8976) },
            { "maple_2_LOD0", new Pair(5313, 10698, "maple_2_LOD1", 3340, 3987) },
            { "oak_2_LOD0", new Pair(7343, 21336, "oak_2_LOD1", 4332, 9534) },
            { "pine_1_LOD0", new Pair(932, 2757, "pine_1_LOD1", 616, 1005) },
            { "pine_3_LOD0", new Pair(2806, 4467, "pine_3_LOD1", 1873, 1986) },
            { "spruce_1_LOD0", new Pair(4606, 14073, "spruce_1_LOD1", 2734, 5328) },
            { "spruce_2_LOD0", new Pair(4469, 13038, "spruce_2_LOD1", 2831, 5736) },
            { "sycamore_LOD0", new Pair(5262, 19410, "sycamore_LOD1", 1546, 2514) },
        };
        public static bool Matches(string high, int highVertices, long highIndices, string low, int lowVertices, long lowIndices) =>
            high != null && pairs.TryGetValue(high, out var p) && p.Low == low && p.HighVertices == highVertices &&
            p.HighIndices == highIndices && p.LowVertices == lowVertices && p.LowIndices == lowIndices;
    }
}
