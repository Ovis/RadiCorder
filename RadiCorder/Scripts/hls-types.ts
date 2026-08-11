/**
 * hls.js 利用箇所で共有する最小限の型定義。
 * 外部ライブラリの型定義を導入せずに any を避けるために利用する。
 */
export interface HlsInstance {
    config: {
        xhrSetup?: (xhr: XMLHttpRequest) => void;
    };
    loadSource(url: string): void;
    attachMedia(media: HTMLMediaElement): void;
    on(eventName: string, callback: () => void): void;
    destroy(): void;
}

export interface HlsLiveInstance extends HlsInstance {
    liveSyncPosition?: number;
    startLoad(startPosition?: number): void;
}

export interface HlsConstructor<TInstance extends HlsInstance> {
    new(): TInstance;
    isSupported(): boolean;
    Events: {
        MANIFEST_PARSED: string;
    };
}

export type HlsWindow<TInstance extends HlsInstance> = Window & {
    Hls?: HlsConstructor<TInstance>;
};
