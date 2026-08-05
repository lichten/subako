import CoreGraphics
import Testing
@testable import SubakoCore

/// タイムライン文字サイズ倍率の段階計算 (Mac 版独自)。UI 非依存の純ロジック。
@Suite struct TimelineFontScaleTests {
    @Test func stepsAreSortedAndContainNormal() {
        #expect(TimelineFontScale.steps == TimelineFontScale.steps.sorted())
        #expect(TimelineFontScale.steps.contains(TimelineFontScale.normal))
    }

    @Test func largerMovesOneStepUp() {
        #expect(TimelineFontScale.larger(than: 1.0) == 1.15)
        #expect(TimelineFontScale.larger(than: 1.15) == 1.3)
    }

    @Test func smallerMovesOneStepDown() {
        #expect(TimelineFontScale.smaller(than: 1.0) == 0.85)
        #expect(TimelineFontScale.smaller(than: 1.3) == 1.15)
    }

    @Test func largestStaysAtLargest() {
        #expect(TimelineFontScale.larger(than: 2.0) == 2.0)
        #expect(TimelineFontScale.isLargest(2.0))
    }

    @Test func smallestStaysAtSmallest() {
        #expect(TimelineFontScale.smaller(than: 0.85) == 0.85)
        #expect(TimelineFontScale.isSmallest(0.85))
    }

    @Test func normalIsNeitherEnd() {
        #expect(!TimelineFontScale.isLargest(TimelineFontScale.normal))
        #expect(!TimelineFontScale.isSmallest(TimelineFontScale.normal))
    }

    // MARK: - normalize (手書き settings.json への耐性)

    @Test func normalizeKeepsKnownSteps() {
        for step in TimelineFontScale.steps {
            #expect(TimelineFontScale.normalize(step) == step)
        }
    }

    @Test func normalizeRoundsToNearestStep() {
        #expect(TimelineFontScale.normalize(1.2) == 1.15)
        #expect(TimelineFontScale.normalize(1.4) == 1.3)
        #expect(TimelineFontScale.normalize(1.7) == 1.75)
    }

    @Test func normalizeClampsOutOfRangeValues() {
        #expect(TimelineFontScale.normalize(0.1) == 0.85)
        #expect(TimelineFontScale.normalize(99) == 2.0)
        #expect(TimelineFontScale.normalize(-5) == 0.85)
    }

    @Test func normalizeRejectsNonFiniteValues() {
        #expect(TimelineFontScale.normalize(.nan) == TimelineFontScale.normal)
        #expect(TimelineFontScale.normalize(.infinity) == TimelineFontScale.normal)
    }

    /// 未知の値からでも段階移動が破綻しない (normalize を経由するため)
    @Test func stepsFromUnknownValueSnapFirst() {
        #expect(TimelineFontScale.larger(than: 1.2) == 1.3)
        #expect(TimelineFontScale.smaller(than: 1.2) == 1.0)
    }
}
