using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators;

public sealed partial class ModelInputGenerator
{
    private const string TaskFqn = "global::System.Threading.Tasks.Task";

    private const string TokenFqn = "global::System.Threading.CancellationToken";

    private const string WritesFqn = "global::Rask.Data.GeneratedModelWrites";

    private const string DbParameter = "global::Microsoft.EntityFrameworkCore.DbContext? db = null, ";

    private const string ApplyDoc = "        /// <param name=\"apply\">Sets values that do not come from the form, after the model's; <c>null</c> for none.</param>";

    private const string DbDoc = "        /// <param name=\"db\">The context to work in, or <c>null</c> to open one. A given context is only STAGED — the caller saves it — and is not disposed.</param>";

    private const string NotFoundDoc = "        /// <exception cref=\"global::System.Collections.Generic.KeyNotFoundException\">No row has the id, or it is soft-deleted.</exception>";

    private const string ConflictDoc = "        /// <exception cref=\"global::Rask.Cqrs.RaskValidationException\">The row was saved by someone else since it was read: a failure about the whole form.</exception>";

    private const string VersionDoc = "        /// <param name=\"version\">The version last read, to refuse a write to a row saved since; <c>null</c> skips the check.</param>";

    // ---- the members on the entity ----
    // Every write ends in the same two optional parameters: `apply`, for values that do not come from the form
    // (a timestamp, the signed-in user), run after the model so it has the last word; and `db`, a context to
    // join instead of opening one.
    private static void AppendWrites(
        StringBuilder s, Entity entity, bool idLessCreate, bool createWithId, bool formCreate, bool formUpdate, bool formModel)
    {
        var entityType = entity.FullyQualifiedName;
        var modelType = FormModelType(entity);
        var modelName = entity.Name + ModelSuffix;
        // The model is named only when it is written: an entity with no form writes gets no model, and a cref to one
        // is CS1574 in an app that builds its documentation (a strict app does).
        s.Append("/// <summary>The writes Rask generates for <see cref=\"").Append(entityType).Append("\" />");
        if (formModel)
        {
            s.Append(", taking its <see cref=\"").Append(modelType).Append("\" />");
        }

        s.AppendLine(".</summary>");
        s.Append(entity.Accessibility).Append(" static class ").Append(modelName).AppendLine("Extensions");
        s.AppendLine("{");

        s.Append("    extension(").Append(entityType).AppendLine(")");
        s.AppendLine("    {");

        // A create takes a form: `Create(model, apply?)` beside `Update(id, model, apply?)`. A row with no form behind
        // it is born by the aggregate's own factory and saved with `Save()`, so its invariants hold from the start.
        if (idLessCreate)
        {
            AppendIdLessCreates(s, entity, formCreate);
        }

        if (createWithId)
        {
            AppendKeyedCreates(s, entity, formCreate);
        }

        if (!entity.IsChild && entity.IdTypeName is { } idType)
        {
            AppendModelUpdate(s, entity, idType, formUpdate);
            AppendApplyUpdate(s, entity, idType);
            AppendDelete(s, entity, idType);
            AppendFind(s, entity, idType);
            AppendModelFill(s, entity, idType, formModel);
        }

        s.AppendLine("    }");
        s.AppendLine();

        if (!entity.IsChild && entity.IdTypeName is not null)
        {
            AppendSave(s, entity);
        }

        if (entity.LoadsById)
        {
            AppendLoad(s, entity);
        }
    }

    // `await id.Load(version, forUpdate: true)` on a typed id one root alone is keyed by: the aggregate loaded whole
    // inside a static operation that writes it, its row locked first when asked.
    private static void AppendLoad(StringBuilder s, Entity entity)
    {
        var entityType = entity.FullyQualifiedName;
        s.Append("    extension(").Append(entity.IdTypeName!.TrimEnd('?')).AppendLine(" id)");
        s.AppendLine("    {");
        s.Append("        /// <summary>Loads the <see cref=\"").Append(entityType)
            .AppendLine("\" /> with this id, whole — its row locked first, to the end of the Db.Transaction, when <paramref name=\"forUpdate\" /> is true.</summary>");
        if (entity.Versioned)
        {
            s.AppendLine("        /// <param name=\"version\">The version the caller read, refused when it has moved on; <c>null</c> asks none.</param>");
        }

        s.AppendLine("        /// <param name=\"forUpdate\">Locks its row first, so nobody changes it before this transaction has.</param>");
        s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the load.</param>");
        s.AppendLine("        /// <returns>The aggregate and its children.</returns>");
        s.AppendLine(NotFoundDoc);
        if (entity.Versioned)
        {
            s.AppendLine(ConflictDoc);
        }

        s.AppendLine("        /// <exception cref=\"global::System.InvalidOperationException\"><paramref name=\"forUpdate\" /> outside a Db.Transaction.</exception>");
        s.Append("        public ").Append(TaskFqn).Append('<').Append(entityType).Append("> Load(")
            .Append(entity.Versioned ? "int? version = null, " : "")
            .Append("bool forUpdate = false, ").Append(TokenFqn).AppendLine(" cancellationToken = default) =>");
        s.Append("            ").Append(WritesFqn).Append(".Load<").Append(entityType).Append(">(id!, ")
            .Append(entity.Versioned ? "version" : "null").AppendLine(", forUpdate, cancellationToken);");
        s.AppendLine("    }");
    }

    private static void AppendIdLessCreates(StringBuilder s, Entity entity, bool formCreate)
    {
        var entityType = entity.FullyQualifiedName;
        var modelType = FormModelType(entity);
        var applyParameter = "global::System.Action<" + entityType + ">? apply = null, ";
        var createMark = s.Length;
        s.Append("        /// <summary>Inserts a new <see cref=\"").Append(entityType)
            .AppendLine("\" /> built from <paramref name=\"model\" />.</summary>");
        s.AppendLine("        /// <param name=\"model\">The values to create it with.</param>");
        s.AppendLine(ApplyDoc);
        s.AppendLine(DbDoc);
        s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the save.</param>");
        s.AppendLine("        /// <returns>The inserted entity, with its key.</returns>");
        AppendKeyRemarks(s, entity);
        s.Append("        public static ").Append(TaskFqn).Append('<').Append(entityType).Append("> Create(")
            .Append(modelType).Append(" model, ").Append(applyParameter).Append(DbParameter).Append(TokenFqn)
            .AppendLine(" cancellationToken = default)");
        s.AppendLine("        {");
        s.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(model);");
        AppendValidateCall(s);
        AppendNewEntity(s, entity, withId: false);
        s.AppendLine("            __Apply(entity, model);");
        s.AppendLine("            apply?.Invoke(entity);");
        AppendReturn(s, WritesFqn + ".Create(entity, db, cancellationToken)");
        s.AppendLine("        }");
        s.AppendLine();

        Keep(s, formCreate, createMark);
    }

    private static void AppendKeyedCreates(StringBuilder s, Entity entity, bool formCreate)
    {
        var entityType = entity.FullyQualifiedName;
        var modelType = FormModelType(entity);
        var applyParameter = "global::System.Action<" + entityType + ">? apply = null, ";
        var keyedMark = s.Length;
        s.Append("        /// <summary>Inserts a new <see cref=\"").Append(entityType)
            .AppendLine("\" /> under <paramref name=\"id\" />, built from <paramref name=\"model\" />.</summary>");
        s.AppendLine("        /// <param name=\"id\">The key of the new row.</param>");
        s.AppendLine("        /// <param name=\"model\">The values to create it with.</param>");
        s.AppendLine(ApplyDoc);
        s.AppendLine(DbDoc);
        s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the save.</param>");
        s.AppendLine("        /// <returns>The inserted entity.</returns>");
        s.Append("        public static ").Append(TaskFqn).Append('<').Append(entityType).Append("> Create(")
            .Append(entity.IdTypeName).Append(" id, ").Append(modelType).Append(" model, ").Append(applyParameter)
            .Append(DbParameter).Append(TokenFqn).AppendLine(" cancellationToken = default)");
        s.AppendLine("        {");
        s.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(model);");
        AppendValidateCall(s);
        AppendNewEntity(s, entity, withId: true);
        s.AppendLine("            __Apply(entity, model);");
        s.AppendLine("            apply?.Invoke(entity);");
        AppendReturn(s, WritesFqn + ".Create(entity, db, cancellationToken)");
        s.AppendLine("        }");
        s.AppendLine();

        Keep(s, formCreate, keyedMark);
    }

    private static void AppendModelUpdate(StringBuilder s, Entity entity, string idType, bool formUpdate)
    {
        var entityType = entity.FullyQualifiedName;
        var modelType = FormModelType(entity);
        var applyParameter = "global::System.Action<" + entityType + ">? apply = null, ";
        var version = entity.Versioned ? "model.Version" : "null";
        var updateMark = s.Length;
        s.Append("        /// <summary>Writes <paramref name=\"model\" /> onto the stored <see cref=\"").Append(entityType)
            .AppendLine("\" /> with <paramref name=\"id\" /> — only the values that changed.</summary>");
        s.AppendLine("        /// <param name=\"id\">The id of the row to update.</param>");
        s.AppendLine("        /// <param name=\"model\">The edited values" +
                     (entity.Versioned ? ", carrying the version they were read at" : "") + ".</param>");
        s.AppendLine(ApplyDoc);
        s.AppendLine(DbDoc);
        s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the load and the save.</param>");
        s.AppendLine("        /// <returns>The updated entity.</returns>");
        s.AppendLine(NotFoundDoc);
        if (entity.Versioned)
        {
            s.AppendLine(ConflictDoc);
        }

        s.Append("        public static ").Append(TaskFqn).Append('<').Append(entityType).Append("> Update(")
            .Append(idType).Append(" id, ").Append(modelType).Append(" model, ").Append(applyParameter)
            .Append(DbParameter).Append(TokenFqn).AppendLine(" cancellationToken = default)");
        s.AppendLine("        {");
        s.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(model);");
        AppendValidateCall(s);
        AppendReturn(
            s,
            WritesFqn + ".Update<" + entityType + ">(id!, " + version
            + ", entity => { __Apply(entity, model); apply?.Invoke(entity); }, db, cancellationToken)");
        s.AppendLine("        }");
        s.AppendLine();

        Keep(s, formUpdate, updateMark);
    }

    // The write with no form behind it: `Product.Update(id, p => p.ShippedAt = now)`, and its awaited twin for a change
    // that reads first: `Supply.Update(id, async s => await s.Distribute(…), version: m.Version)`.
    private static void AppendApplyUpdate(StringBuilder s, Entity entity, string idType)
    {
        AppendApplyUpdate(s, entity, idType, "global::System.Action<" + entity.FullyQualifiedName + ">",
            "Sets the new values on the loaded row.");
        AppendApplyUpdate(s, entity, idType, "global::System.Func<" + entity.FullyQualifiedName + ", " + TaskFqn + ">",
            "Changes the loaded row; it may await reads of its own.");
    }

    private static void AppendApplyUpdate(StringBuilder s, Entity entity, string idType, string applyType, string applyDoc)
    {
        var entityType = entity.FullyQualifiedName;
        s.Append("        /// <summary>Loads the stored <see cref=\"").Append(entityType)
            .AppendLine("\" /> with <paramref name=\"id\" />, applies <paramref name=\"apply\" /> and saves — only the values that changed.</summary>");
        s.AppendLine("        /// <param name=\"id\">The id of the row to update.</param>");
        s.Append("        /// <param name=\"apply\">").Append(applyDoc).AppendLine("</param>");
        if (entity.Versioned)
        {
            s.AppendLine(VersionDoc);
        }

        s.AppendLine(DbDoc);
        s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the load and the save.</param>");
        s.AppendLine("        /// <returns>The updated entity.</returns>");
        s.AppendLine(NotFoundDoc);
        if (entity.Versioned)
        {
            s.AppendLine(ConflictDoc);
        }

        s.Append("        public static ").Append(TaskFqn).Append('<').Append(entityType).Append("> Update(")
            .Append(idType).Append(" id, ").Append(applyType).Append(" apply, ");
        if (entity.Versioned)
        {
            s.Append("int? version = null, ");
        }

        s.Append(DbParameter).Append(TokenFqn).AppendLine(" cancellationToken = default) =>");
        s.Append("            ").Append(WritesFqn).Append(".Update<").Append(entityType).Append(">(id!, ")
            .Append(entity.Versioned ? "version" : "null").AppendLine(", apply, db, cancellationToken);");
        s.AppendLine();
    }

    // An aggregate that names neither Deletable nor SoftDelete is cancelled or archived through its own methods, so a
    // delete it cannot be asked for is one nobody can call by mistake.
    private static void AppendDelete(StringBuilder s, Entity entity, string idType)
    {
        var entityType = entity.FullyQualifiedName;
        var deleteMark = s.Length;
        s.Append("        /// <summary>Deletes the stored <see cref=\"").Append(entityType)
            .AppendLine("\" /> with <paramref name=\"id\" />, through the interceptors.</summary>");
        s.AppendLine("        /// <param name=\"id\">The id of the row to delete.</param>");
        if (entity.Versioned)
        {
            s.AppendLine(VersionDoc);
        }

        s.AppendLine(DbDoc);
        s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the load and the save.</param>");
        s.AppendLine(NotFoundDoc);
        s.Append("        public static ").Append(TaskFqn).Append(" Delete(").Append(idType).Append(" id, ");
        if (entity.Versioned)
        {
            s.Append("int? version = null, ");
        }

        s.Append(DbParameter).Append(TokenFqn).AppendLine(" cancellationToken = default) =>");
        s.Append("            ").Append(WritesFqn).Append(".Delete<").Append(entityType).Append(">(id!, ")
            .Append(entity.Versioned ? "version" : "null").AppendLine(", db, cancellationToken);");
        s.AppendLine();
        Keep(s, entity.Deletable, deleteMark);
    }

    // The aggregate itself, to call its methods and `Save()`: detached, its snapshot remembered, so no `db:` — a
    // query by key, not EF's Find, so a soft-deleted or another tenant's row is not found here either.
    private static void AppendFind(StringBuilder s, Entity entity, string idType)
    {
        var entityType = entity.FullyQualifiedName;
        s.Append("        /// <summary>Loads the <see cref=\"").Append(entityType)
            .AppendLine("\" /> with <paramref name=\"id\" />, whole, to change through its methods and <c>Save()</c>.</summary>");
        s.AppendLine("        /// <param name=\"id\">The id of the aggregate.</param>");
        s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the load.</param>");
        s.AppendLine("        /// <returns>The aggregate and its children, or <c>null</c> when none has that id or it is soft-deleted.</returns>");
        s.Append("        public static ").Append(TaskFqn).Append('<').Append(entityType).Append("?> Find(")
            .Append(idType).Append(" id, ").Append(TokenFqn).AppendLine(" cancellationToken = default) =>");
        s.Append("            ").Append(WritesFqn).Append(".Find<").Append(entityType)
            .AppendLine(">(id!, cancellationToken);");
        s.AppendLine();

        if (!entity.Versioned)
        {
            return;
        }

        // The form's version: a row saved since the form was filled is refused here, before anything is changed.
        s.Append("        /// <summary>Loads the <see cref=\"").Append(entityType)
            .AppendLine("\" /> with <paramref name=\"id\" />, whole — refused when it is no longer at <paramref name=\"version\" />, the one the form was filled from.</summary>");
        s.AppendLine("        /// <param name=\"id\">The id of the aggregate.</param>");
        s.AppendLine("        /// <param name=\"version\">The version the form was filled from.</param>");
        s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the load.</param>");
        s.AppendLine("        /// <returns>The aggregate and its children, or <c>null</c> when none has that id or it is soft-deleted.</returns>");
        s.AppendLine(ConflictDoc);
        s.Append("        public static ").Append(TaskFqn).Append('<').Append(entityType).Append("?> Find(")
            .Append(idType).Append(" id, int version, ").Append(TokenFqn).AppendLine(" cancellationToken = default) =>");
        s.Append("            ").Append(WritesFqn).Append(".Find<").Append(entityType)
            .AppendLine(">(id!, version, cancellationToken);");
        s.AppendLine();
    }

    // `product.Save()`: inserted when it has no row yet, otherwise only what changed since Find read it.
    private static void AppendSave(StringBuilder s, Entity entity)
    {
        s.Append("    extension(").Append(entity.FullyQualifiedName).AppendLine(" entity)");
        s.AppendLine("    {");
        s.AppendLine("        /// <summary>Saves this aggregate as it now stands: inserted when it has no row yet, otherwise written over it — only what changed, children synced by id.</summary>");
        s.AppendLine(DbDoc);
        s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the load and the save.</param>");
        s.AppendLine("        /// <exception cref=\"global::System.Collections.Generic.KeyNotFoundException\">Its row has been soft-deleted since it was read.</exception>");
        if (entity.Versioned)
        {
            s.AppendLine("        /// <exception cref=\"global::Rask.Cqrs.RaskValidationException\">Its row was saved by someone else since it was read: a failure about the whole form.</exception>");
        }

        s.Append("        public ").Append(TaskFqn).Append(" Save(").Append(DbParameter).Append(TokenFqn)
            .AppendLine(" cancellationToken = default) =>");
        s.Append("            ").Append(WritesFqn).AppendLine(".Save(entity, db, cancellationToken);");
        AppendInstanceDelete(s, entity);
        s.AppendLine("    }");
        s.AppendLine();
    }

    // `file.Delete()`: the aggregate the caller holds, deleted from what Find read — no read first. Only where the
    // static Delete(id) is, so an aggregate retired through its own methods has neither.
    private static void AppendInstanceDelete(StringBuilder s, Entity entity)
    {
        var deleteMark = s.Length;
        s.AppendLine();
        s.AppendLine("        /// <summary>Deletes this aggregate from what was read, with no read first: stamped when it soft-deletes, otherwise its row and its children removed.</summary>");
        s.AppendLine(DbDoc);
        s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the save.</param>");
        s.AppendLine("        /// <exception cref=\"global::System.InvalidOperationException\">It has never been saved.</exception>");
        s.AppendLine("        /// <exception cref=\"global::Rask.Cqrs.RaskValidationException\">Its row was changed or deleted by someone else since it was read.</exception>");
        s.Append("        public ").Append(TaskFqn).Append(" Delete(").Append(DbParameter).Append(TokenFqn)
            .AppendLine(" cancellationToken = default) =>");
        s.Append("            ").Append(WritesFqn).Append(".Delete<").Append(entity.FullyQualifiedName)
            .AppendLine(">(entity, db, cancellationToken);");
        Keep(s, entity.Deletable, deleteMark);
    }

    private static string JoinWrites(List<string> writes) =>
        writes.Count == 1
            ? writes[0]
            : string.Join(", ", writes.Take(writes.Count - 1)) + " or " + writes[writes.Count - 1];
}
