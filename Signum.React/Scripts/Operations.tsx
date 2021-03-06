﻿import * as React from "react"
import { Router, Route, Redirect } from "react-router"
import { Button, OverlayTrigger, Tooltip, MenuItem, DropdownButton } from "react-bootstrap"
import { Dic } from './Globals';
import { ajaxGet, ajaxPost } from './Services';
import { openModal } from './Modals';
import {
    Lite, Entity, ModifiableEntity, EmbeddedEntity, LiteMessage, OperationMessage, EntityPack,
    OperationSymbol, ConstructSymbol_From, ConstructSymbol_FromMany, ConstructSymbol_Simple, ExecuteSymbol, DeleteSymbol
} from './Signum.Entities';
import { OperationLogEntity } from './Signum.Entities.Basics';
import { PropertyRoute, PseudoType, EntityKind, TypeInfo, IType, Type, getTypeInfo, OperationInfo, OperationType, GraphExplorer } from './Reflection';
import { TypeContext, EntityFrame } from './TypeContext';
import * as Finder from './Finder';
import * as Navigator from './Navigator';
import * as QuickLinks from './QuickLinks';
import * as ContexualItems from './SearchControl/ContextualItems';
import ButtonBar from './Frames/ButtonBar';
import { getEntityOperationButtons, defaultOnClick } from './Operations/EntityOperations';
import { getConstructFromManyContextualItems, getEntityOperationsContextualItems, defaultContextualClick } from './Operations/ContextualOperations';
import { ContextualItemsContext} from './SearchControl/ContextualItems';

export function start() {
    ButtonBar.onButtonBarRender.push(getEntityOperationButtons);
    ContexualItems.onContextualItems.push(getConstructFromManyContextualItems);
    ContexualItems.onContextualItems.push(getEntityOperationsContextualItems);
    QuickLinks.registerGlobalQuickLink(ctx => new QuickLinks.QuickLinkExplore({
        queryName: OperationLogEntity,
        parentColumn: "Target",
        parentValue: ctx.lite
    }, { isVisible: getTypeInfo(ctx.lite.EntityType) && getTypeInfo(ctx.lite.EntityType).requiresSaveOperation }));
}

export const operationSettings: { [operationKey: string]: OperationSettings } = {};

export function addSettings(...settings: OperationSettings[]) {
    settings.forEach(s => Dic.addOrThrow(operationSettings, s.operationSymbol.key!, s));
}


export function getSettings(operation: OperationSymbol | string): OperationSettings | undefined {
    const operationKey = (operation as OperationSymbol).key || operation as string;

    return operationSettings[operationKey];
}

export const isOperationAllowedEvent: Array<(oi: OperationInfo | OperationSymbol | string) => boolean> = [];

export function isOperationAllowed(oi: OperationInfo | OperationSymbol | string) {
    return isOperationAllowedEvent.every(a => a(oi));
}

export function assertOperationAllowed(operation: OperationInfo | OperationSymbol | string) {
    var key = (operation as OperationInfo | OperationSymbol).key || operation as string;
    if (!isOperationAllowed(key))
        throw new Error(`Operation ${key} is denied`);
}


export function operationInfos(ti: TypeInfo) {
    return Dic.getValues(ti.operations!).filter(isOperationAllowed);
}

/**
 * Operation Settings
 */
export abstract class OperationSettings {

    text?: () => string;
    operationSymbol: OperationSymbol;

    constructor(operationSymbol: OperationSymbol) {
        this.operationSymbol = operationSymbol;
    }
}



/**
 * Constructor Operation Settings
 */
export class ConstructorOperationSettings<T extends Entity> extends OperationSettings {

    isVisible?: (ctx: ConstructorOperationContext<T>) => boolean;
    onConstruct?: (ctx: ConstructorOperationContext<T>) => Promise<EntityPack<T> | undefined> | undefined;

    constructor(operationSymbol: ConstructSymbol_Simple<T>, options: ConstructorOperationOptions<T>) {
        super(operationSymbol);

        Dic.assign(this, options);
    }
}

export interface ConstructorOperationOptions<T extends Entity> {
    text?: () => string;
    isVisible?: (ctx: ConstructorOperationContext<T>) => boolean;
    onConstruct?: (ctx: ConstructorOperationContext<T>) => Promise<EntityPack<T> | undefined> | undefined;
}

export class ConstructorOperationContext<T extends Entity> {
    operationInfo: OperationInfo;
    settings: ConstructorOperationSettings<T>;
    typeInfo: TypeInfo;

    defaultConstruct(...args: any[]): Promise<EntityPack<T> | undefined> {
        return API.construct<T>(this.typeInfo.name, this.operationInfo.key, ...args);
    }
}



/**
 * Contextual Operation Settings
 */
export class ContextualOperationSettings<T extends Entity> extends OperationSettings {

    isVisible?: (ctx: ContextualOperationContext<T>) => boolean;
    hideOnCanExecute?: boolean;
    confirmMessage?: (ctx: ContextualOperationContext<T>) => string;
    onClick?: (ctx: ContextualOperationContext<T>) => void;
    style?: BsStyle;
    icon?: string;
    iconColor?: string;
    order?: number;

    constructor(operationSymbol: ConstructSymbol_FromMany<any, T>, options: ContextualOperationOptions<T>) {
        super(operationSymbol);

        Dic.assign(this, options);
    }
}

export interface ContextualOperationOptions<T extends Entity> {
    text?: () => string;
    isVisible?: (ctx: ContextualOperationContext<T>) => boolean;
    hideOnCanExecute?: boolean;
    confirmMessage?: (ctx: ContextualOperationContext<T>) => string;
    onClick?: (ctx: ContextualOperationContext<T>) => void;
    style?: BsStyle;
    icon?: string;
    iconColor?: string;
    order?: number;
}

export class ContextualOperationContext<T extends Entity> {
    context: ContextualItemsContext<T>
    operationInfo: OperationInfo;
    settings?: ContextualOperationSettings<T>; 
    entityOperationSettings?: EntityOperationSettings<T>;
    canExecute?: string;
    event?: React.MouseEvent<any>;
    defaultContextualClick(...args: any[]) {
        defaultContextualClick(this, ...args);
    }
}

export class EntityOperationContext<T extends Entity> {

    static fromTypeContext<T extends Entity>(ctx: TypeContext<T>, operation: ExecuteSymbol<T> | DeleteSymbol<T> | ConstructSymbol_From<T, any>): EntityOperationContext<T>
    {
        if (!ctx.frame)
            throw new Error("a frame is necessary");
        var result = new EntityOperationContext<T>();
        result.frame = ctx.frame;
        result.entity = ctx.value;
        result.settings = getSettings(operation) as EntityOperationSettings<T>;
        result.operationInfo = getTypeInfo(ctx.value.Type).operations![operation.key!];
        result.canExecute = undefined;
        return result;
    }
    frame: EntityFrame<T>;
    tag?: string;
    entity: T;
    operationInfo: OperationInfo;
    settings: EntityOperationSettings<T>;
    canExecute?: string;
    closeRequested?: boolean;
    event?: React.MouseEvent<any>;
    defaultClick(...args: any[]) {
        defaultOnClick(this, ...args);
    }
}

export class EntityOperationSettings<T extends Entity> extends OperationSettings {

    contextual?: ContextualOperationSettings<T>;
    contextualFromMany?: ContextualOperationSettings<T>;

    isVisible?: (ctx: EntityOperationContext<T>) => boolean;
    confirmMessage?: (ctx: EntityOperationContext<T>) => string;
    onClick?: (ctx: EntityOperationContext<T>) => void;
    hideOnCanExecute?: boolean;
    group?: EntityOperationGroup | null;
    order?: number;
    style?: BsStyle;
    withClose?: boolean;

    constructor(operationSymbol: ExecuteSymbol<T> | DeleteSymbol<T> | ConstructSymbol_From<any, T>, options: EntityOperationOptions<T>) {
        super(operationSymbol)

        Dic.assign(this, options);

        this.contextual = options.contextual ? new ContextualOperationSettings(operationSymbol as any, options.contextual) : undefined;
        this.contextualFromMany = options.contextualFromMany ? new ContextualOperationSettings(operationSymbol as any, options.contextualFromMany) : undefined;
    }
}

export type BsStyle = "default" | "primary" | "success" | "info" | "warning" | "danger";

export interface EntityOperationOptions<T extends Entity> {
    contextual?: ContextualOperationOptions<T>;
    contextualFromMany?: ContextualOperationOptions<T>;

    text?: () => string;
    isVisible?: (ctx: EntityOperationContext<T>) => boolean;
    confirmMessage?: (ctx: EntityOperationContext<T>) => string;
    onClick?: (ctx: EntityOperationContext<T>) => void;
    hideOnCanExecute?: boolean;
    group?: EntityOperationGroup | null;
    order?: number;
    style?: BsStyle;
    withClose?: boolean;
}

export const CreateGroup: EntityOperationGroup = {
    key: "create",
    text: () => OperationMessage.Create.niceToString(),
    simplifyName: cs => {
        const array = new RegExp(OperationMessage.CreateFromRegex.niceToString()).exec(cs);
        return array ? array[1].firstUpper() : cs;
    },
    cssClass: "sf-operation",
    order: 200,
};

export interface EntityOperationGroup {
    key: string;
    text: () => string;
    simplifyName?: (complexName: string) => string;
    cssClass?: string;
    order?: number;
}


export function setIsSaveFunction(isSaveFunction: (oi: OperationInfo) => boolean) {
    isSave = isSaveFunction;
}

export let isSave = (oi: OperationInfo): boolean => {
    return oi.key.endsWith(".Save");
}

export function autoStyleFunction(oi: OperationInfo): BsStyle {
    return oi.operationType == OperationType.Delete ? "danger" :
        oi.operationType == OperationType.Execute && isSave(oi) ? "primary" : "default";
}


export function isEntityOperation(operationType: OperationType) {
    return operationType == OperationType.ConstructorFrom ||
        operationType == OperationType.Execute ||
        operationType == OperationType.Delete;
}

export namespace API {

    export function construct<T extends Entity>(type: string, operationKey: string | ConstructSymbol_Simple<T>, ...args: any[]): Promise<EntityPack<T>> {
        return ajaxPost<EntityPack<T>>({ url: "~/api/operation/construct" }, { operationKey: getOperationKey(operationKey), args, type });
    }

    export function constructFromEntity<T extends Entity, F extends Entity>(entity: F, operationKey: string | ConstructSymbol_From<T, F>, ...args: any[]): Promise<EntityPack<T>> {
        GraphExplorer.propagateAll(entity, args);
        return ajaxPost<EntityPack<T>>({ url: "~/api/operation/constructFromEntity" }, { entity: entity, operationKey: getOperationKey(operationKey), args: args } as EntityOperationRequest);
    }

    export function constructFromLite<T extends Entity, F extends Entity>(lite: Lite<F>, operationKey: string | ConstructSymbol_From<T, F>, ...args: any[]): Promise<EntityPack<T>> {
        GraphExplorer.propagateAll(lite, args);
        return ajaxPost<EntityPack<T>>({ url: "~/api/operation/constructFromLite" }, { lite: lite, operationKey: getOperationKey(operationKey), args: args } as LiteOperationRequest);
    }

    export function constructFromMultiple<T extends Entity, F extends Entity>(lites: Lite<F>[], operationKey: string | ConstructSymbol_From<T, F>, ...args: any[]): Promise<ErrorReport> {
        GraphExplorer.propagateAll(lites, args);
        return ajaxPost<ErrorReport>({ url: "~/api/operation/constructFromMultiple" }, { lites: lites, operationKey: getOperationKey(operationKey), args: args } as MultiOperationRequest);
    }

    export function constructFromMany<T extends Entity, F extends Entity>(lites: Lite<F>[], operationKey: string | ConstructSymbol_From<T, F>, ...args: any[]): Promise<EntityPack<T>> {
        GraphExplorer.propagateAll(lites, args);
        return ajaxPost<EntityPack<T>>({ url: "~/api/operation/constructFromMany" }, { lites: lites, operationKey: getOperationKey(operationKey), args: args } as MultiOperationRequest);
    }

    export function executeEntity<T extends Entity>(entity: T, operationKey: string | ExecuteSymbol<T>, ...args: any[]): Promise<EntityPack<T>> {
        GraphExplorer.propagateAll(entity, args);
        return ajaxPost<EntityPack<T>>({ url: "~/api/operation/executeEntity" }, { entity: entity, operationKey: getOperationKey(operationKey), args: args } as EntityOperationRequest);
    }

    export function executeLite<T extends Entity>(lite: Lite<T>, operationKey: string | ExecuteSymbol<T>, ...args: any[]): Promise<EntityPack<T>> {
        GraphExplorer.propagateAll(lite, args);
        return ajaxPost<EntityPack<T>>({ url: "~/api/operation/executeLite" }, { lite: lite, operationKey: getOperationKey(operationKey), args: args } as LiteOperationRequest);
    }

    export function executeMultiple<T extends Entity>(lites: Lite<T>[], operationKey: string | ExecuteSymbol<T>, ...args: any[]): Promise<ErrorReport> {
        GraphExplorer.propagateAll(lites, args);
        return ajaxPost<ErrorReport>({ url: "~/api/operation/executeMultiple" }, { lites: lites, operationKey: getOperationKey(operationKey), args: args } as MultiOperationRequest);
    }

    export function deleteEntity<T extends Entity>(entity: T, operationKey: string | DeleteSymbol<T>, ...args: any[]): Promise<void> {
        GraphExplorer.propagateAll(entity, args);
        return ajaxPost<void>({ url: "~/api/operation/deleteEntity" }, { entity: entity, operationKey: getOperationKey(operationKey), args: args } as EntityOperationRequest);
    }

    export function deleteLite<T extends Entity>(lite: Lite<T>, operationKey: string | DeleteSymbol<T>, ...args: any[]): Promise<void> {
        GraphExplorer.propagateAll(lite, args);
        return ajaxPost<void>({ url: "~/api/operation/deleteLite" }, { lite: lite, operationKey: getOperationKey(operationKey), args: args } as LiteOperationRequest);
    }

    export function deleteMultiple<T extends Entity>(lites: Lite<T>[], operationKey: string | DeleteSymbol<T>, ...args: any[]): Promise<ErrorReport> {
        GraphExplorer.propagateAll(lites, args);
        return ajaxPost<ErrorReport>({ url: "~/api/operation/deleteMultiple" }, { lites: lites, operationKey: getOperationKey(operationKey), args: args } as MultiOperationRequest);
    }

    export interface ErrorReport {
        errors: { [liteKey: string]: string; }
    }




    export function getOperationKey(operationKey: string | OperationSymbol) {
        return (operationKey as OperationSymbol).key || operationKey as string;
    }



    export interface MultiOperationRequest {
        operationKey: string;
        type?: string;
        lites: Lite<Entity>[];
        args: any[]
    }

    interface ConstructOperationRequest {
        operationKey: string;
        type?: string;
        args: any[];
    }


    interface EntityOperationRequest {
        operationKey: string;
        entity: Entity;
        type?: string;
        args: any[];
    }

    interface LiteOperationRequest {
        operationKey: string;
        lite: Lite<Entity>;
        type?: string;
        args: any[];
    }


    export function stateCanExecutes<T extends Entity>(lites: Lite<T>[], operationKeys: string[]): Promise<CanExecutesResponse> {
        return ajaxPost<CanExecutesResponse>({ url: "~/api/operation/stateCanExecutes" }, { lites, operationKeys });
    }

    export interface CanExecutesResponse {
        canExecutes: { [operationKey: string]: string };
    }
}


