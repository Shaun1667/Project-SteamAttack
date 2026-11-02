using UnityEngine;
using UnityEngine.SceneManagement;

namespace NGH
{
    /// <summary>
    /// 씬 바닥이 돌(박석·궁궐 바닥)인지 눈인지 위에서 내려다본 격자로 기록한 지도. 발자국 소리를 고를 때 씀.
    ///  - 바닥 충돌체는 대부분 '눈 덮인 지면' 하나라 충돌체로는 가를 수 없어, 돌 바닥 메시가 덮은 칸을 미리 구워 둠
    ///  - 굽기: 메뉴 NGH/발소리 지면 지도 굽기 (Editor/FootSurfaceMapBaker). Resources/NGH_FootSurface_{씬 이름} 으로 저장
    ///  - 지도가 없는 씬은 모두 돌(기본 발소리)로 봄
    /// </summary>
    public class FootSurfaceMap : ScriptableObject
    {
        public string sceneName;
        public Vector2 origin;          // 격자 (0,0) 칸의 최소 x,z
        public float cellSize = 1f;
        public int width, height;
        public byte[] stone;            // 1 = 돌 바닥, 0 = 눈
        [Tooltip("이 높이(m)보다 높은 곳에 서 있으면 돌로 봄 (월대·계단·기단 위)")]
        public float raisedHeight = 0.45f;

        static FootSurfaceMap _cached;
        static string _cachedScene;

        public bool IsStone(Vector3 world)
        {
            if (world.y > raisedHeight) return true;
            if (stone == null) return true;
            int x = Mathf.FloorToInt((world.x - origin.x) / cellSize);
            int z = Mathf.FloorToInt((world.z - origin.y) / cellSize);
            if (x < 0 || z < 0 || x >= width || z >= height) return false;   // 지도 밖은 눈밭
            return stone[z * width + x] != 0;
        }

        /// <summary>지금 씬에서 이 위치가 눈밭인지 (지도가 없으면 false)</summary>
        public static bool IsSnow(Vector3 world)
        {
            string scene = SceneManager.GetActiveScene().name;
            if (_cachedScene != scene)
            {
                _cachedScene = scene;
                _cached = Resources.Load<FootSurfaceMap>("NGH_FootSurface_" + scene);
            }
            return _cached != null && !_cached.IsStone(world);
        }
    }
}
