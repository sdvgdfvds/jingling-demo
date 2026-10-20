using UnityEngine;

namespace NexusAnima
{
    public class SpiritProjectile : MonoBehaviour
    {
        SpiritUnit _source;
        SpiritUnit _target;
        bool _fromSkill;
        float _speed;
        float _delay;
        ElementType _element;
        float _cellSize = 0.24f;
        BattleManager _battle;

        public void Initialize(
            SpiritUnit source,
            SpiritUnit target,
            bool fromSkill,
            float travelTime,
            float delay,
            ElementType element = ElementType.Fire,
            float cellSize = 0.24f)
        {
            _source = source;
            _target = target;
            _fromSkill = fromSkill;
            _delay = Mathf.Max(0f, delay);
            _element = element;
            _cellSize = cellSize;
            _battle = Object.FindAnyObjectByType<BattleManager>();

            float distance = target != null
                ? Vector3.Distance(transform.position, target.transform.position)
                : 0.1f;
            _speed = distance / Mathf.Max(0.05f, travelTime);
        }

        void Update()
        {
            // Hold mid-flight while Xinxiang presentation freezes the board.
            if (_battle != null && _battle.CombatFrozen)
                return;

            if (_delay > 0f)
            {
                _delay -= Time.deltaTime;
                return;
            }

            // Drop shot if target died, source gone, or target is still grass-ambushing (untargetable).
            if (_target == null || !_target.IsAlive || _source == null || _target.IsAmbushing)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 destination = _target.transform.position + Vector3.up * (_cellSize * 0.38f);
            Vector3 toTarget = destination - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);

            transform.position = Vector3.MoveTowards(
                transform.position,
                destination,
                _speed * Time.deltaTime);

            if ((transform.position - destination).sqrMagnitude <= 0.0004f)
            {
                AttackVfxLibrary.SpawnImpact(_element, destination, _cellSize);
                _source.ApplyProjectileHit(_target, _fromSkill);
                Destroy(gameObject);
            }
        }

    }
}
