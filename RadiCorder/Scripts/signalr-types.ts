export interface SignalRHubConnection {
    on(methodName: string, newMethod: (...args: unknown[]) => void): void;
    onreconnected(callback: (connectionId?: string) => void): void;
    onclose(callback: (error?: Error) => void): void;
    start(): Promise<void>;
    stop(): Promise<void>;
}

export interface SignalRHubConnectionBuilder {
    withUrl(url: string): SignalRHubConnectionBuilder;
    withAutomaticReconnect(): SignalRHubConnectionBuilder;
    configureLogging(logLevel: unknown): SignalRHubConnectionBuilder;
    build(): SignalRHubConnection;
}

export interface SignalRNamespace {
    HubConnectionBuilder: new () => SignalRHubConnectionBuilder;
    LogLevel: {
        Warning: unknown;
    };
}

export type SignalRWindow = Window & {
    signalR?: SignalRNamespace;
};
